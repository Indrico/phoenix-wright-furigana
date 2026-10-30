using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class PwaatCecilPatch
{
    private static MethodDefinition Find(TypeDefinition type, string name, int parameters)
    {
        foreach (MethodDefinition method in type.Methods)
            if (method.Name == name && method.Parameters.Count == parameters) return method;
        throw new InvalidDataException("Missing expected method: " + type.FullName + "." + name);
    }

    public static void Apply(string originalPath, string runtimePath, string managedPath, string outputPath)
    {
        using (DefaultAssemblyResolver resolver = new DefaultAssemblyResolver())
        {
            resolver.AddSearchDirectory(managedPath);
            ReaderParameters options = new ReaderParameters();
            options.AssemblyResolver = resolver;
            using (AssemblyDefinition original = AssemblyDefinition.ReadAssembly(originalPath, options))
            using (AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(runtimePath, options))
            {
                ModuleDefinition module = original.MainModule;
                TypeDefinition layer = runtime.MainModule.GetType("PwaatFurigana.FuriganaLayer");
                MethodDefinition awake = Find(module.GetType("mainCtrl"), "Awake", 0);
                MethodDefinition setCharacter = Find(module.GetType("MessageSystem"), "SetCharacter", 2);
                ILProcessor il = awake.Body.GetILProcessor();
                il.InsertBefore(awake.Body.Instructions[0], il.Create(OpCodes.Call, module.ImportReference(Find(layer, "Install", 0))));
                il = setCharacter.Body.GetILProcessor();
                Instruction entry = setCharacter.Body.Instructions[0];
                il.InsertBefore(entry, il.Create(OpCodes.Ldarg_1));
                il.InsertBefore(entry, il.Create(OpCodes.Call, module.ImportReference(Find(layer, "BeginLine", 1))));
                original.Write(outputPath);
            }
        }
    }
}
