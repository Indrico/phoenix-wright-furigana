using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

const string originalHash = "8c419d19c86812497cabf9e29b2c87651156a2d705176dadbfec9b066747d15f";
if (args.Length != 2 || args[0] is not ("apply" or "restore" or "verify"))
    throw new ArgumentException("Usage: Patcher apply|restore|verify GAME_DIRECTORY");
string root = Path.GetFullPath(args[1]);
string mod = Path.Combine(root, "furigana-mod");
string target = Path.Combine(root, "PWAAT_Data/Managed/Assembly-CSharp.dll");
string backup = Path.Combine(mod, "backups/Assembly-CSharp.dll");
string manifest = Path.Combine(mod, "evidence/patch.json");
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
void CheckCurrent()
{
    if (Hash(target) == originalHash) return;
    if (!File.Exists(manifest) || Hash(target) != JsonDocument.Parse(File.ReadAllText(manifest)).RootElement.GetProperty("patched_sha256").GetString())
        throw new InvalidOperationException("The game assembly differs from this supported build. No files changed.");
}
CheckCurrent();
if (args[0] == "restore")
{
    if (Hash(target) == originalHash) { Console.WriteLine("Original assembly already installed."); return; }
    if (Hash(backup) != originalHash) throw new InvalidDataException("Original backup hash mismatch");
    File.Copy(backup, target, true);
    Console.WriteLine("Original game assembly restored; furigana is disabled.");
    return;
}
Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
if (!File.Exists(backup))
{
    if (Hash(target) != originalHash) throw new InvalidOperationException("Original backup is missing");
    File.Copy(target, backup);
}
if (Hash(backup) != originalHash) throw new InvalidDataException("Original backup hash mismatch");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(target));
var options = new ReaderParameters { AssemblyResolver = resolver };
using var baseline = AssemblyDefinition.ReadAssembly(backup, options);
var hooks = new Dictionary<string, string>
{
    ["System.Void mainCtrl::Awake()"] = "Install",
    ["System.Void MessageSystem::SetCharacter(MessageWork,System.Char)"] = "BeginLine"
};
IEnumerable<MethodDefinition> Methods(ModuleDefinition module) => module.GetTypes().SelectMany(t => t.Methods);
string Body(MethodDefinition method) => !method.HasBody ? "" : string.Join("\n", method.Body.Instructions.Select(i => i.ToString()));
void Verify(AssemblyDefinition assembly)
{
    var before = Methods(baseline.MainModule).ToDictionary(m => m.FullName);
    var after = Methods(assembly.MainModule).ToDictionary(m => m.FullName);
    if (!before.Keys.Order().SequenceEqual(after.Keys.Order())) throw new InvalidDataException("Method inventory changed");
    var changed = before.Keys.Where(k => Body(before[k]) != Body(after[k])).Order().ToArray();
    if (!changed.SequenceEqual(hooks.Keys.Order())) throw new InvalidDataException("Unexpected changed methods: " + string.Join(", ", changed));
    foreach (var hook in hooks)
    {
        var method = after[hook.Key];
        int calls = method.Body.Instructions.Count(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference r
            && r.DeclaringType.FullName == "PwaatFurigana.FuriganaLayer" && r.Name == hook.Value);
        if (calls != 1) throw new InvalidDataException("Missing or duplicate hook: " + hook.Key);
    }
    Console.WriteLine($"Verified: exactly {changed.Length} methods patched; {before.Count - changed.Length} method bodies unchanged.");
}
if (args[0] == "verify")
{
    using var current = AssemblyDefinition.ReadAssembly(target, options);
    Verify(current);
    string runtimeFile = Path.Combine(root, "PWAAT_Data/Managed/PwaatFurigana.dll");
    var record = JsonDocument.Parse(File.ReadAllText(manifest)).RootElement;
    if (Hash(runtimeFile) != record.GetProperty("runtime_sha256").GetString())
        throw new InvalidDataException("Runtime DLL hash mismatch");
    using var runtimeCheck = AssemblyDefinition.ReadAssembly(runtimeFile, options);
    if (runtimeCheck.MainModule.RuntimeVersion != baseline.MainModule.RuntimeVersion)
        throw new InvalidDataException("Runtime CLR version differs from the game");
    foreach (var reference in runtimeCheck.MainModule.AssemblyReferences)
        if (reference.Name != "Assembly-CSharp" && !File.Exists(Path.Combine(Path.GetDirectoryName(target)!, reference.Name + ".dll")))
            throw new InvalidDataException("Missing runtime dependency: " + reference.FullName);
    string readingsFile = Path.Combine(root, "PWAAT_Data/StreamingAssets/Furigana/readings.tsv");
    var dataRecord = JsonDocument.Parse(File.ReadAllText(Path.Combine(mod, "evidence/readings-build.json"))).RootElement;
    if (Hash(readingsFile) != dataRecord.GetProperty("readings_sha256").GetString())
        throw new InvalidDataException("Reading data hash mismatch");
    Console.WriteLine("Original backup SHA-256 verified.");
    Console.WriteLine("Runtime CLR version, dependency files, runtime hash, and reading-data hash verified.");
    return;
}
string runtimePath = Path.Combine(root, "PWAAT_Data/Managed/PwaatFurigana.dll");
using var runtime = AssemblyDefinition.ReadAssembly(runtimePath, options);
var layer = runtime.MainModule.GetType("PwaatFurigana.FuriganaLayer");
foreach (var hook in hooks)
{
    var method = Methods(baseline.MainModule).Single(m => m.FullName == hook.Key);
    var entry = method.Body.Instructions[0];
    var il = method.Body.GetILProcessor();
    if (hook.Value == "BeginLine") il.InsertBefore(entry, il.Create(OpCodes.Ldarg_1));
    il.InsertBefore(entry, il.Create(OpCodes.Call, baseline.MainModule.ImportReference(layer.Methods.Single(m => m.Name == hook.Value))));
}
string staging = target + ".furigana-tmp";
baseline.Write(staging);
// Compare a fresh read of the staged output to the untouched backup.
using (var original = AssemblyDefinition.ReadAssembly(backup, options))
using (var staged = AssemblyDefinition.ReadAssembly(staging, options))
{
    var oldBodies = Methods(original.MainModule).ToDictionary(m => m.FullName, Body);
    var differences = Methods(staged.MainModule).Where(m => oldBodies[m.FullName] != Body(m)).Select(m => m.FullName).Order();
    if (!differences.SequenceEqual(hooks.Keys.Order())) throw new InvalidDataException("Staged patch modified unexpected methods");
}
Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
File.WriteAllText(manifest, JsonSerializer.Serialize(new
{
    original_sha256 = originalHash,
    patched_sha256 = Hash(staging),
    runtime_sha256 = Hash(runtimePath),
    hooks,
    engine = "Mono.Cecil 0.11.6",
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
File.Move(staging, target, true);
Console.WriteLine("Installed Japanese furigana toggle. Original assembly retained in furigana-mod/backups.");
