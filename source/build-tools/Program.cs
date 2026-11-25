using System.Security.Cryptography;
using System.Text.Json;
using ILRuntime.Mono.Cecil;
using ILRuntime.Mono.Cecil.Cil;

static class Program
{
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static IEnumerable<TypeDefinition> Types(TypeDefinition type)
    {
        yield return type;
        foreach (var child in type.NestedTypes)
            foreach (var descendant in Types(child)) yield return descendant;
    }
    static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    static void Main(string[] args)
    {
        if (args[0] == "collection-types")
        {
            using var declarations = ModuleDefinition.ReadModule(args[1]);
            var names = new[] { "Proto.Level", "Proto.BuffType", "Proto.LevelConfig", "Proto.BattleTalentData", "Proto.CardConfig", "Proto.KeYinCardConfig", "Proto.ProtoArrayInt32" };
            Save(args[2], names.Select(name => {
                var type = declarations.Types.Single(t => t.FullName == name);
                return new { name, isEnum = type.IsEnum, enumStorage = type.IsEnum ? type.Fields.Single(f => f.Name == "value__").FieldType.FullName : null,
                    baseType = type.BaseType.FullName, interfaces = type.Interfaces.Select(i => i.InterfaceType.FullName).ToArray() };
            }).ToArray());
            return;
        }
        if (args[0] == "reference")
        {
            var nativeResolver = new DefaultAssemblyResolver();
            nativeResolver.AddSearchDirectory(Path.GetDirectoryName(args[1]));
            nativeResolver.AddSearchDirectory(Path.Combine(Path.GetDirectoryName(args[1]), "managed"));
            using var module = ModuleDefinition.ReadModule(args[1], new ReaderParameters { AssemblyResolver = nativeResolver });
            var changes = new List<string>();
            foreach (var spec in new[] {
                ("BattleExecuter", "m_CurrentHuiHeCount", "System.Int32"),
                ("BattleCharacterUI", "m_StatusBar", "UnityEngine.RectTransform"),
                ("BattleCharacter", "m_IsDead", "System.Boolean"),
            })
            {
                var field = module.Types.Single(t => t.FullName == spec.Item1).Fields.Single(f => f.Name == spec.Item2);
                if (field.FieldType.FullName != spec.Item3 || field.IsStatic) throw new InvalidDataException("Native field declaration changed: " + field.FullName);
                changes.Add(field.FullName);
                field.Attributes = (field.Attributes & ~FieldAttributes.FieldAccessMask) | FieldAttributes.Public;
            }
            module.Write(args[2]);
            Save(args[3], new { scope = "Compile-only reference; never package or install this DLL", input = Hash(args[1]), output = Hash(args[2]), fields = changes });
            return;
        }
        if (args[0] != "audit") throw new ArgumentException("Expected reference or audit");
        var resolver = new DefaultAssemblyResolver();
        foreach (var dir in args.Skip(3)) resolver.AddSearchDirectory(dir);
        using var output = ModuleDefinition.ReadModule(args[1], new ReaderParameters { AssemblyResolver = resolver });
        var errors = new List<string>();
        int methods = 0, fields = 0, calls = 0, arrays = 0;
        bool ReflectionType(string name) => name == "System.Type" || name == "System.RuntimeTypeHandle" || name.StartsWith("System.Reflection.", StringComparison.Ordinal) || name == "System.Activator" || name.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal);
        foreach (var type in output.GetTypeReferences())
            if (ReflectionType(type.FullName)) errors.Add("Runtime metadata type: " + type.FullName);
        foreach (var reference in output.AssemblyReferences)
            if (reference.Name == "System.Private.CoreLib" || reference.Name == "System.Runtime" || reference.Name == "System.Collections" || reference.Version.Major > 4 && reference.Name.StartsWith("System", StringComparison.Ordinal))
                errors.Add("Desktop framework reference: " + reference.FullName);
        foreach (var type in output.Types.SelectMany(Types))
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            methods++;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Ldtoken)
                {
                    if (!(instruction.Operand is FieldReference blob) || blob.DeclaringType.Name != "<PrivateImplementationDetails>" || blob.Resolve().RVA == 0)
                        errors.Add("Runtime metadata token: " + method.FullName);
                    else arrays++;
                }
                if (instruction.Operand is MethodReference called)
                {
                    calls++;
                    if (ReflectionType(called.DeclaringType.FullName) || called.Name == "GetType" && called.DeclaringType.FullName == "System.Object")
                        errors.Add("Runtime reflection call: " + called.FullName);
                    try { if (called.Resolve() == null) errors.Add("Unresolved method: " + called.FullName); }
                    catch (Exception e) { errors.Add("Unresolved method: " + called.FullName + " " + e.Message); }
                }
                if (instruction.Operand is FieldReference field)
                {
                    fields++;
                    try { if (field.Resolve() == null) errors.Add("Unresolved field: " + field.FullName); }
                    catch (Exception e) { errors.Add("Unresolved field: " + field.FullName + " " + e.Message); }
                }
            }
        }
        Save(args[2], new { pass = errors.Count == 0, sha256 = Hash(args[1]), methods, calls, fields, arrayInitializers = arrays, references = output.AssemblyReferences.Select(r => r.FullName).ToArray(), errors = errors.Distinct().ToArray() });
        if (errors.Count != 0) throw new InvalidDataException("Final runtime audit failed: " + string.Join("\n", errors.Distinct().Take(20)));
        Console.WriteLine("PASS final runtime: no reflection, all direct methods/fields resolve against original game and SDK references");
    }
}
