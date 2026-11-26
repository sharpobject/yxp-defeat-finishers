using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ILRuntime.Mono.Cecil;
using ILRuntime.Mono.Cecil.Cil;

// Build-time only. Compare the actual emitted operands with the Windows player,
// not with the larger Mono reference assemblies used by the C# compiler.
sealed class WindowsNativeAudit : IDisposable
{
    readonly ModuleDefinition plugin;
    readonly Dictionary<string, ModuleDefinition> native = new(StringComparer.Ordinal);
    readonly Dictionary<string, TypeDefinition> types = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<JsonElement>> bodies = new(StringComparer.Ordinal);
    readonly HashSet<string> closedTypes = new(StringComparer.Ordinal);
    readonly Dictionary<Finding,List<string>> pendingClosedTypes = new();
    readonly Dictionary<string,string> signatureTypeProofs = new(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<string>> shortTypeNames = new(StringComparer.Ordinal);
    readonly Dictionary<string, object> dependencies = new(StringComparer.Ordinal);
    readonly Dictionary<string, Finding> findings = new(StringComparer.Ordinal);
    readonly JsonDocument manifestDocument, scriptDocument;
    readonly JsonElement manifest;
    readonly string manifestHash;
    static readonly HashSet<string> Interpreted = new(StringComparer.Ordinal) { "DarkSun.HotUpdate", "Yx.ModSdk.Core", "Yx.ModSdk" };
    static readonly Dictionary<string,string> Aliases = new() {
        ["System.Void"]="void", ["System.Boolean"]="bool", ["System.Char"]="char", ["System.Byte"]="byte", ["System.SByte"]="sbyte",
        ["System.Int16"]="short", ["System.UInt16"]="ushort", ["System.Int32"]="int", ["System.UInt32"]="uint", ["System.Int64"]="long",
        ["System.UInt64"]="ulong", ["System.Single"]="float", ["System.Double"]="double", ["System.String"]="string", ["System.Object"]="object" };
    sealed class Finding
    {
        public string kind { get; set; }
        public string operand { get; set; }
        public string status { get; set; }
        public string reason { get; set; }
        public string declaration { get; set; }
        public string native_method { get; set; }
        public long[] addresses { get; set; } = Array.Empty<long>();
        public int occurrences { get; set; }
        public List<object> callers { get; set; } = new();
    }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static IEnumerable<TypeDefinition> AllTypes(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(AllTypes));
    static string VerifyFile(JsonElement row)
    {
        string path = row.GetProperty("path").GetString();
        if (Hash(path) != row.GetProperty("sha256").GetString()) throw new InvalidDataException("Windows native input differs: " + path);
        return path;
    }
    public WindowsNativeAudit(string path, ModuleDefinition plugin)
    {
        this.plugin = plugin;
        manifestHash = Hash(path);
        manifestDocument = JsonDocument.Parse(File.ReadAllBytes(path)); manifest = manifestDocument.RootElement;
        if (string.IsNullOrWhiteSpace(manifest.GetProperty("steam_build_id").ToString())) throw new InvalidDataException("Windows Steam build ID missing");
        VerifyFile(manifest.GetProperty("game_assembly")); VerifyFile(manifest.GetProperty("global_metadata"));
        VerifyFile(manifest.GetProperty("hot_update"));
        string script = VerifyFile(manifest.GetProperty("script"));
        using var production = JsonDocument.Parse(File.ReadAllBytes(VerifyFile(manifest.GetProperty("provenance").GetProperty("native_dump"))));
        foreach (string input in new[]{"game_assembly", "global_metadata"})
            if (production.RootElement.GetProperty("inputs").GetProperty(input).GetProperty("sha256").GetString() != manifest.GetProperty(input).GetProperty("sha256").GetString())
                throw new InvalidDataException("Windows dump was produced from different " + input);
        var produced = production.RootElement.GetProperty("outputs");
        if (produced.GetProperty("script.json").GetString() != manifest.GetProperty("script").GetProperty("sha256").GetString() || production.RootElement.GetProperty("exit_code").GetInt32() != 0)
            throw new InvalidDataException("Windows method inventory is not the successful native dump output");
        string directory = manifest.GetProperty("dummy_dll_directory").GetString();
        var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(directory);
        foreach (var row in manifest.GetProperty("dummy_dll_sha256").EnumerateObject())
        {
            string file = Path.Combine(directory, row.Name);
            if (Path.GetFileName(row.Name) != row.Name || Hash(file) != row.Value.GetString()) throw new InvalidDataException("Windows declaration DLL differs: " + file);
            if (produced.GetProperty("DummyDll/" + row.Name).GetString() != row.Value.GetString()) throw new InvalidDataException("Windows declaration DLL is not the bound native dump output: " + row.Name);
            var module = ModuleDefinition.ReadModule(file, new ReaderParameters { AssemblyResolver = resolver });
            native.Add(module.Assembly.Name.Name, module);
            foreach (var type in module.Types.SelectMany(AllTypes))
            {
                types.Add(module.Assembly.Name.Name + "|" + type.FullName, type);
                if(!shortTypeNames.TryGetValue(type.Name,out var identities))shortTypeNames.Add(type.Name,identities=new());
                identities.Add(module.Assembly.Name.Name+"|"+type.FullName);
            }
        }
        if (Directory.GetFiles(directory, "*.dll").Length != native.Count) throw new InvalidDataException("Unbound Windows declaration DLL population");
        scriptDocument = JsonDocument.Parse(File.ReadAllBytes(script));
        foreach (var row in scriptDocument.RootElement.GetProperty("ScriptMethod").EnumerateArray())
        {
            string name = row.GetProperty("Name").GetString();
            if (!bodies.TryGetValue(name, out var list)) bodies.Add(name, list = new());
            list.Add(row);
            if (row.GetProperty("Address").GetInt64()!=0) closedTypes.Add(name.Split("$$")[0]);
        }
        foreach (var row in scriptDocument.RootElement.GetProperty("ScriptMetadata").EnumerateArray())
            if (row.GetProperty("Address").GetInt64()!=0)
            { string name=row.GetProperty("Name").GetString(); if(name.EndsWith("_TypeInfo"))closedTypes.Add(name[..^9]); else if(name.EndsWith("_var"))closedTypes.Add(name[..^4]); }
    }
    bool IsInterpreted(ModuleDefinition module)
    {
        if (module.Assembly.Name.Name == plugin.Assembly.Name.Name) return true;
        if (!Interpreted.Contains(module.Assembly.Name.Name)) return false;
        if (!dependencies.ContainsKey(module.Assembly.Name.Name))
        {
            string hash = Hash(module.FileName);
            if (module.Assembly.Name.Name == "DarkSun.HotUpdate" && hash != manifest.GetProperty("hot_update").GetProperty("sha256").GetString())
                throw new InvalidDataException("Interpreted HotUpdate differs from Windows target");
            dependencies.Add(module.Assembly.Name.Name, new { path = module.FileName, sha256 = hash });
        }
        return true;
    }
    static string Shape(TypeReference type)
    {
        if (type is GenericParameter p) return (p.Type == GenericParameterType.Method ? "!!" : "!") + p.Position;
        if (type is GenericInstanceType g) return Shape(g.ElementType) + "<" + string.Join(",", g.GenericArguments.Select(Shape)) + ">";
        if (type is ArrayType a) return Shape(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]";
        if (type is ByReferenceType r) return Shape(r.ElementType) + "&";
        if (type is PointerType q) return Shape(q.ElementType) + "*";
        // Resolve forwarded framework declarations (for example netstandard's
        // System.Single) before comparing their actual defining assembly.
        string assembly=type.Resolve()?.Module.Assembly.Name.Name ?? throw new InvalidDataException("Unresolved type identity: "+type.FullName);
        return assembly+"|"+type.FullName;
    }
    static string Shape(MethodReference method) => method.Name + "|" + method.HasThis + "|" + method.GenericParameters.Count + "|" + Shape(method.ReturnType) + "(" + string.Join(",", method.Parameters.Select(p => Shape(p.ParameterType))) + ")";
    TypeDefinition NativeType(TypeDefinition definition)
    {
        types.TryGetValue(definition.Module.Assembly.Name.Name + "|" + definition.FullName, out var found); return found;
    }
    Finding Start(string kind, string operand, MethodDefinition caller, Instruction instruction, out bool first, string identity = null)
    {
        string key = kind + "|" + (identity ?? operand);
        first = !findings.TryGetValue(key, out var finding);
        if (first) { finding = new Finding { kind = kind, operand = operand, status = "error" }; findings.Add(key, finding); }
        finding.occurrences++;
        if (caller != null && finding.callers.Count < 8) finding.callers.Add(new { method = caller.FullName, il_offset = "IL_" + instruction.Offset.ToString("x4"), opcode = instruction.OpCode.Name });
        return finding;
    }
    public void Type(TypeReference reference, MethodDefinition caller = null, Instruction instruction = null)
    {
        if (reference is GenericParameter) return;
        if (reference is GenericInstanceType generic)
        {
            Type(generic.ElementType,caller,instruction); foreach(var argument in generic.GenericArguments)Type(argument,caller,instruction);
            var closed = Start("closed-type",reference.FullName,caller,instruction,out bool firstClosed,Shape(reference)); if(!firstClosed)return;
            try
            {
                var definition=generic.Resolve();
                if(IsInterpreted(definition.Module)){closed.status="interpreted";return;}
                var arguments=generic.GenericArguments.Select(NativeArgument).ToArray();
                var candidates=new List<string>();
                string name=Regex.Replace(definition.FullName.Replace('/','.'),"`[0-9]+","");
                foreach(bool share in new[]{false,true})
                {
                    try { foreach(var argument in arguments)UnambiguousArgument(share?Share(argument,plugin.TypeSystem.Object):argument); }
                    catch(InvalidDataException e){closed.reason=e.Message;continue;}
                    string concrete=name+"<"+string.Join(", ",arguments.Select(t=>Simple(t,share)))+">";
                    candidates.Add(concrete);
                    if(closedTypes.Contains(concrete)){closed.status=share?"native-reference-shared-type":"native-closed-type";closed.declaration=concrete;return;}
                }
                closed.reason??="Mapped closed native type absent from TypeInfo/type-variable and concrete owner-body inventory";
                pendingClosedTypes.Add(closed,candidates);
            }
            catch(Exception e){closed.reason=e.Message;}
            return;
        }
        if (reference is TypeSpecification spec) { Type(spec.ElementType,caller,instruction); return; }
        var row = Start("type", reference.FullName, caller, instruction, out bool first,Shape(reference)); if (!first) return;
        try
        {
            var definition = reference.Resolve();
            if (definition == null) { row.reason = "Unresolved compile-time type"; return; }
            if (IsInterpreted(definition.Module)) { row.status = "interpreted"; return; }
            var found = NativeType(definition);
            if (found == null) { row.reason = "Type absent from Windows metadata"; return; }
            if (definition.IsValueType != found.IsValueType || definition.GenericParameters.Count != found.GenericParameters.Count) { row.reason = "Windows type kind or generic arity differs"; return; }
            row.status = "native-declaration"; row.declaration = found.FullName;
        }
        catch (Exception e) { row.reason = e.Message; }
    }
    static TypeReference Substitute(TypeReference type, IList<TypeReference> typeArgs, IList<TypeReference> methodArgs)
    {
        if (type is GenericParameter p)
        { var list = p.Type == GenericParameterType.Method ? methodArgs : typeArgs; return p.Position < list.Count ? list[p.Position] : type; }
        if (type is GenericInstanceType g) { var result = new GenericInstanceType(g.ElementType); foreach (var argument in g.GenericArguments) result.GenericArguments.Add(Substitute(argument,typeArgs,methodArgs)); return result; }
        if (type is ArrayType a) return new ArrayType(Substitute(a.ElementType,typeArgs,methodArgs),a.Rank);
        if (type is ByReferenceType b) return new ByReferenceType(Substitute(b.ElementType,typeArgs,methodArgs));
        if (type is PointerType q) return new PointerType(Substitute(q.ElementType,typeArgs,methodArgs));
        return type;
    }
    IList<TypeReference> OwnerArguments(TypeReference owner, TypeDefinition declaration)
    {
        for (int depth = 0; depth != 32 && owner != null; depth++)
        {
            var args = owner is GenericInstanceType g ? (IList<TypeReference>)g.GenericArguments : Array.Empty<TypeReference>();
            var definition = owner.Resolve();
            if (Shape(definition) == Shape(declaration)) return args;
            if(!IsInterpreted(definition.Module))
            {
                var actual=NativeType(definition);
                if(actual==null || (actual.BaseType==null)!=(definition.BaseType==null) || actual.BaseType!=null && Shape(actual.BaseType)!=Shape(definition.BaseType))
                    throw new InvalidDataException("Windows receiver inheritance differs before declaring member: "+definition.FullName);
            }
            owner = definition.BaseType == null ? null : Substitute(definition.BaseType,args,Array.Empty<TypeReference>());
        }
        throw new InvalidDataException("Cannot resolve the actual declaring type for inherited member");
    }
    static string Simple(TypeReference t, bool share)
    {
        if (t is GenericParameter) throw new InvalidDataException("Open generic native call requires a concrete call-site specialization");
        if (share && !t.IsValueType && t is not ByReferenceType && t is not PointerType) return "object";
        if (t is ArrayType a) return Simple(a.ElementType,share) + "[" + new string(',',a.Rank-1) + "]";
        if (t is ByReferenceType b) return Simple(b.ElementType,share) + "&";
        if (t is PointerType p) return Simple(p.ElementType,share) + "*";
        if (Aliases.TryGetValue(t.FullName,out string alias)) return alias;
        if (t is GenericInstanceType g) return Regex.Replace(g.ElementType.Name,"`[0-9]+","") + "<" + string.Join(", ",g.GenericArguments.Select(x=>Simple(x,false))) + ">";
        return t.Name;
    }
    TypeReference NativeArgument(TypeReference type)
    {
        if (type is GenericParameter) throw new InvalidDataException("Open generic native call requires a concrete call-site specialization");
        if (type is ArrayType array) return new ArrayType(NativeArgument(array.ElementType),array.Rank);
        if (type is ByReferenceType reference) return new ByReferenceType(NativeArgument(reference.ElementType));
        if (type is PointerType pointer) return new PointerType(NativeArgument(pointer.ElementType));
        var definition = type.Resolve();
        if (!IsInterpreted(definition.Module))
        {
            if(type is GenericInstanceType g){var result=new GenericInstanceType(g.ElementType);foreach(var a in g.GenericArguments)result.GenericArguments.Add(NativeArgument(a));return result;}
            return type;
        }
        if (definition.IsEnum) return NativeArgument(definition.Fields.Single(f=>f.Name=="value__").FieldType);
        for (var ancestor = definition.BaseType; ancestor != null; ancestor = ancestor.Resolve().BaseType)
        {
            var parent = ancestor.Resolve();
            if (!IsInterpreted(parent.Module) && parent.FullName != "System.Object" && parent.FullName != "System.ValueType" && parent.FullName != "System.Enum")
                throw new InvalidDataException("Interpreted native-derived generic argument requires its registered cross-binding adaptor: " + type.FullName);
        }
        // ILType.TypeForCLR uses this reference type for ordinary interpreted
        // classes and non-enum structs. Their names are not native type names.
        return types["ILRuntime|ILRuntime.Runtime.Intepreter.ILTypeInstance"];
    }
    void UnambiguousArgument(TypeReference type)
    {
        if(Aliases.ContainsKey(type.FullName))return;
        if(type is TypeSpecification specification && type is not GenericInstanceType){UnambiguousArgument(specification.ElementType);return;}
        var element=type is GenericInstanceType generic?generic.ElementType:type;
        if(!shortTypeNames.TryGetValue(element.Name,out var matches) || matches.Count!=1)
            throw new InvalidDataException("Native generic argument short name is absent/ambiguous in the dump; full type association required: "+Shape(type));
        if(type is GenericInstanceType arguments)foreach(var argument in arguments.GenericArguments)UnambiguousArgument(argument);
    }
    static string BodyName(MethodDefinition definition, IList<TypeReference> ownerArgs, IList<TypeReference> methodArgs, bool share)
    {
        string owner = Regex.Replace(definition.DeclaringType.FullName.Replace('/','.'),"`[0-9]+","");
        if (ownerArgs.Count != 0) owner += "<" + string.Join(", ",ownerArgs.Select(t=>Simple(t,share))) + ">";
        string method = definition.Name;
        if (methodArgs.Count != 0) method += "<" + string.Join(", ",methodArgs.Select(t=>Simple(t,share))) + ">";
        return owner + "$$" + method;
    }
    static string[] BodyParameters(JsonElement body)
    {
        string signature = body.GetProperty("Signature").GetString();
        int start = signature.IndexOf('('), end = signature.LastIndexOf(')');
        if (start < 0 || end < start) throw new InvalidDataException("Unsupported native signature: " + signature);
        return signature.Substring(start+1,end-start-1).Split(',').Select(x=>x.Trim()).Where(x=>x.Length!=0).ToArray();
    }
    static string Identifier(string value) => Regex.Replace(value,@"[^A-Za-z0-9_]","_");
    static string Display(TypeReference type, bool template)
    {
        if (type is GenericParameter p) return p.Name;
        if (Aliases.TryGetValue(type.FullName,out string alias)) return alias;
        if (type is GenericInstanceType g) return Regex.Replace(g.ElementType.FullName.Replace('/','.'),"`[0-9]+","") + "<" + string.Join(", ",g.GenericArguments.Select(a=> a is GenericParameter ? a.Name : Simple(a,false))) + ">";
        return type.FullName.Replace('/','.');
    }
    static string CType(TypeReference type)
    {
        if (type is GenericParameter) throw new InvalidDataException("Unsubstituted generic ABI type");
        if (type is ByReferenceType b) return CType(b.ElementType) + "*";
        if (type is PointerType p) return CType(p.ElementType) + "*";
        if (type is ArrayType a) return Identifier(Display(a.ElementType,false)) + "_array*";
        string scalar = type.FullName switch {
            "System.Void"=>"void", "System.Boolean"=>"bool", "System.Char"=>"uint16_t", "System.Byte"=>"uint8_t", "System.SByte"=>"int8_t",
            "System.Int16"=>"int16_t", "System.UInt16"=>"uint16_t", "System.Int32"=>"int32_t", "System.UInt32"=>"uint32_t",
            "System.Int64"=>"int64_t", "System.UInt64"=>"uint64_t", "System.Single"=>"float", "System.Double"=>"double",
            "System.IntPtr"=>"intptr_t", "System.UIntPtr"=>"uintptr_t", "System.Object"=>"Il2CppObject*", _=>null };
        if (scalar != null) return scalar;
        if (type.Resolve().IsEnum) return CType(type.Resolve().Fields.Single(f=>f.Name=="value__").FieldType);
        // String uses its namespace in generated C declarations, unlike the
        // short C# aliases used within generic names.
        string display = type.FullName=="System.String" ? "System.String" : Display(type,false);
        return Identifier(display) + "_o" + (type.IsValueType ? "" : "*");
    }
    static string TemplateCType(TypeReference type)
    {
        if (type is ByReferenceType b) return TemplateCType(b.ElementType) + "*";
        if (type is GenericInstanceType g && g.ContainsGenericParameter)
            return Identifier(Display(g,true)) + "_o" + (g.IsValueType ? "" : "*");
        return null;
    }
    static TypeReference Share(TypeReference type, TypeReference objectType)
    {
        if (!type.IsValueType && type is not ByReferenceType && type is not PointerType) return objectType;
        if (type is ByReferenceType b)return new ByReferenceType(Share(b.ElementType,objectType));
        if (type is PointerType p)return new PointerType(Share(p.ElementType,objectType));
        return type;
    }
    static bool AbiTypeMatch(string actual, TypeReference declaration, IList<TypeReference> ownerArgs, IList<TypeReference> methodArgs)
    {
        var concrete = Substitute(declaration,ownerArgs,methodArgs);
        return actual == CType(concrete) || actual == TemplateCType(declaration);
    }
    static bool ParametersMatch(JsonElement body, MethodDefinition method, IList<TypeReference> ownerArgs, IList<TypeReference> methodArgs)
    {
        var all = BodyParameters(body); int offset = method.IsStatic ? 0 : 1;
        if (all.Length != method.Parameters.Count + offset + 1) return false;
        string signature = body.GetProperty("Signature").GetString();
        string returnType = signature.Substring(0,signature.IndexOf(' '));
        if (!AbiTypeMatch(returnType,method.ReturnType,ownerArgs,methodArgs)) return false;
        if(!method.IsStatic)
        {
            TypeReference owner=method.DeclaringType;
            if(ownerArgs.Count!=0){var instance=new GenericInstanceType(owner);foreach(var argument in ownerArgs)instance.GenericArguments.Add(argument);owner=instance;}
            string actual=all[0].EndsWith(" __this")?all[0][..^7]:"";
            string template=null;
            if(method.DeclaringType.HasGenericParameters){var instance=new GenericInstanceType(method.DeclaringType);foreach(var argument in method.DeclaringType.GenericParameters)instance.GenericArguments.Add(argument);template=TemplateCType(instance);}
            if(actual!=CType(owner) && actual!=template)return false;
        }
        for (int i=0;i<method.Parameters.Count;i++)
        {
            var match = Regex.Match(all[i+offset],@"^(.+)\s+([A-Za-z_][A-Za-z_0-9]*)$");
            if (!match.Success || match.Groups[2].Value != method.Parameters[i].Name || !AbiTypeMatch(match.Groups[1].Value,method.Parameters[i].ParameterType,ownerArgs,methodArgs)) return false;
        }
        return true;
    }
    void ProveSignatureTypes(MethodDefinition declaration, IList<TypeReference> ownerArgs, IList<TypeReference> methodArgs, string body)
    {
        void Visit(TypeReference type)
        {
            if(type is GenericInstanceType generic)
            {
                // This optional proof must not conflate identically named native
                // arguments. Their own type check still requires exact/shared
                // evidence; the already verified method body remains valid.
                try {foreach(var argument in generic.GenericArguments)UnambiguousArgument(argument);}
                catch(InvalidDataException){return;}
                string name=Regex.Replace(generic.ElementType.FullName.Replace('/','.'),"`[0-9]+","")+"<"+string.Join(", ",generic.GenericArguments.Select(a=>Simple(a,false)))+">";
                closedTypes.Add(name);signatureTypeProofs.TryAdd(name,body);
                foreach(var argument in generic.GenericArguments)Visit(argument);
            }
            else if(type is TypeSpecification specification)Visit(specification.ElementType);
        }
        Visit(Substitute(declaration.ReturnType,ownerArgs,methodArgs));
        foreach(var parameter in declaration.Parameters)Visit(Substitute(parameter.ParameterType,ownerArgs,methodArgs));
    }
    void FinishTypeProofs()
    {
        foreach(var pending in pendingClosedTypes)
            foreach(string candidate in pending.Value)
                if(signatureTypeProofs.TryGetValue(candidate,out string body))
                {pending.Key.status="native-signature-closed-type";pending.Key.declaration=candidate;pending.Key.native_method=body;pending.Key.reason=null;break;}
    }
    public void Method(MethodReference reference, MethodDefinition definition, MethodDefinition caller, Instruction instruction)
    {
        var constraint=instruction.Previous?.OpCode==OpCodes.Constrained?instruction.Previous.Operand as TypeReference:null;
        string identity=Shape(reference.DeclaringType)+"|"+Shape(reference)+"|"+instruction.OpCode.Name+"|"+(constraint==null?"":Shape(constraint));
        if(reference is GenericInstanceMethod constructed)identity+="|"+string.Join(",",constructed.GenericArguments.Select(Shape));
        var row = Start("method", reference.FullName,caller,instruction,out bool first,identity); if (!first) return;
        try
        {
            if (definition == null) { row.reason = "Unresolved compile-time method"; return; }
            var signatureOwner=OwnerArguments(reference.DeclaringType,definition.DeclaringType);
            var signatureMethod=reference is GenericInstanceMethod signatureGeneric?(IList<TypeReference>)signatureGeneric.GenericArguments:Array.Empty<TypeReference>();
            Type(Substitute(definition.ReturnType,signatureOwner,signatureMethod),caller,instruction);
            foreach(var parameter in definition.Parameters)Type(Substitute(parameter.ParameterType,signatureOwner,signatureMethod),caller,instruction);
            if(constraint!=null)
            {
                if(instruction.OpCode!=OpCodes.Callvirt || !definition.DeclaringType.IsInterface){row.reason="Unsupported constrained native dispatch";return;}
                var receiver=constraint.Resolve();
                if(IsInterpreted(receiver.Module) || !receiver.IsValueType){row.reason="Constrained receiver is not a concrete native value type";return;}
                var nativeReceiver=NativeType(receiver);
                if(nativeReceiver==null || !nativeReceiver.Interfaces.Any(i=>Shape(i.InterfaceType)==Shape(definition.DeclaringType))){row.reason="Windows constrained receiver does not declare the requested interface";return;}
                var implementations=receiver.Methods.Where(m=>m.Name==definition.Name && Shape(m)==Shape(definition)).ToArray();
                if(implementations.Length!=1){row.reason="Constrained interface implementation is not a unique direct receiver declaration";return;}
                definition=implementations[0];
                var concrete=new MethodReference(definition.Name,definition.ReturnType,constraint){HasThis=definition.HasThis,ExplicitThis=definition.ExplicitThis,CallingConvention=definition.CallingConvention};
                foreach(var parameter in definition.Parameters)concrete.Parameters.Add(new ParameterDefinition(parameter.Name,parameter.Attributes,parameter.ParameterType));
                reference=concrete;
            }
            if (IsInterpreted(definition.Module)) { row.status = "interpreted"; return; }
            var owner = NativeType(definition.DeclaringType);
            if (owner == null) { row.reason = "Declaring type absent from Windows metadata"; return; }
            var matches = owner.Methods.Where(x=>Shape(x)==Shape(definition)).ToArray();
            if (matches.Length != 1) { row.reason = matches.Length == 0 ? "Method declaration absent from Windows metadata" : "Ambiguous Windows method declaration"; return; }
            var method = matches[0]; row.declaration = method.FullName;
            if (method.IsPrivate || method.IsConstructor && !method.IsPublic) { row.reason = "Method is inaccessible to ILRuntime CLR lookup"; return; }
            if (method.IsStatic != definition.IsStatic || method.IsPublic != definition.IsPublic || method.IsFamily != definition.IsFamily)
            { row.reason = "Windows method calling convention/access differs"; return; }
            if (method.IsAbstract || method.DeclaringType.IsInterface)
            {
                if (instruction.OpCode != OpCodes.Callvirt) { row.reason = "Abstract/interface member lacks direct body"; return; }
                row.status = "native-virtual-declaration"; return;
            }
            var ownerArgs = OwnerArguments(reference.DeclaringType,definition.DeclaringType).Select(NativeArgument).ToArray();
            var methodArgs = reference is GenericInstanceMethod g ? g.GenericArguments.Select(NativeArgument).ToArray() : Array.Empty<TypeReference>();
            bool generic = ownerArgs.Length != 0 || methodArgs.Length != 0;
            if (!generic)
            {
                var address = method.CustomAttributes.SingleOrDefault(a=>a.AttributeType.Name=="AddressAttribute");
                var rva = address?.Fields.SingleOrDefault(f=>f.Name=="RVA").Argument.Value as string;
                if (string.IsNullOrEmpty(rva) || Convert.ToInt64(rva.Replace("0x",""),16)==0) { row.reason = "Windows declaration has no native body address"; return; }
                long value = Convert.ToInt64(rva.Replace("0x",""),16);
                string name = BodyName(definition,ownerArgs,methodArgs,false);
                if (!bodies.TryGetValue(name,out var declaredBodies) || !declaredBodies.Any(b=>b.GetProperty("Address").GetInt64()==value)) { row.reason = "Native body address absent from Windows method inventory"; row.native_method = name; return; }
                row.status = "native-body"; row.native_method = name; row.addresses = new[] {value}; ProveSignatureTypes(method,ownerArgs,methodArgs,name); return;
            }
            foreach (bool share in new[]{false,true})
            {
                var actualOwnerArgs = share ? ownerArgs.Select(t=>Share(t,plugin.TypeSystem.Object)).ToArray() : ownerArgs;
                var actualMethodArgs = share ? methodArgs.Select(t=>Share(t,plugin.TypeSystem.Object)).ToArray() : methodArgs;
                try {foreach(var argument in actualOwnerArgs.Concat(actualMethodArgs))UnambiguousArgument(argument);}
                catch(InvalidDataException e){row.reason=e.Message;continue;}
                string name = BodyName(definition,ownerArgs,methodArgs,share); row.native_method = name;
                if (!bodies.TryGetValue(name,out var candidates)) continue;
                var exact = candidates.Where(b=>b.GetProperty("Address").GetInt64()!=0 && ParametersMatch(b,method,actualOwnerArgs,actualMethodArgs)).ToArray();
                if (exact.Length == 0) continue;
                if (exact.Length > 1) { row.reason = "Ambiguous closed generic native overload; signature comparison required"; return; }
                if(owner.Methods.Count(other=>other.Name==method.Name && other.GenericParameters.Count==method.GenericParameters.Count && ParametersMatch(exact[0],other,actualOwnerArgs,actualMethodArgs))!=1)
                {row.reason="Native C signature cannot distinguish metadata overloads";return;}
                row.status = share ? "native-reference-shared-body" : "native-closed-generic-body";
                row.addresses = exact.Select(b=>b.GetProperty("Address").GetInt64()).ToArray(); ProveSignatureTypes(method,actualOwnerArgs,actualMethodArgs,name); return;
            }
            row.reason ??= "Closed generic native method body absent (including valid reference-type sharing)";
        }
        catch(Exception e) { row.status="error"; row.reason = e.Message; }
    }
    public void Field(FieldReference reference, FieldDefinition definition, MethodDefinition caller, Instruction instruction)
    {
        var row = Start("field",reference.FullName,caller,instruction,out bool first,Shape(reference.DeclaringType)+"|"+reference.Name+"|"+Shape(reference.FieldType)+"|"+instruction.OpCode.Name); if (!first) return;
        try
        {
            if (definition == null) { row.reason = "Unresolved compile-time field"; return; }
            if (IsInterpreted(definition.Module)) { row.status = "interpreted"; return; }
            OwnerArguments(reference.DeclaringType,definition.DeclaringType);
            var owner = NativeType(definition.DeclaringType);
            var match = owner?.Fields.SingleOrDefault(f=>f.Name==definition.Name && Shape(f.FieldType)==Shape(definition.FieldType) && f.IsStatic==definition.IsStatic);
            if (match == null) { row.reason = "Field declaration absent or different in Windows metadata"; return; }
            if (!match.IsPublic && !match.IsFamily) { row.reason = "Native field is not public/protected; ILRuntime CLR field access is unavailable without an evidenced native binder"; return; }
            row.status = "native-declaration"; row.declaration = match.FullName;
        }
        catch(Exception e) { row.reason = e.Message; }
    }
    public object Report { get { FinishTypeProofs(); return new {
        pass = findings.Values.All(x=>x.status!="error"), manifest_sha256 = manifestHash,
        steam_build_id = manifest.GetProperty("steam_build_id").ToString(),
        game_assembly_sha256 = manifest.GetProperty("game_assembly").GetProperty("sha256").GetString(),
        global_metadata_sha256 = manifest.GetProperty("global_metadata").GetProperty("sha256").GetString(),
        hot_update_sha256 = manifest.GetProperty("hot_update").GetProperty("sha256").GetString(),
        script_sha256 = manifest.GetProperty("script").GetProperty("sha256").GetString(),
        interpreted_dependencies = dependencies,
        concrete_signature_type_proofs = signatureTypeProofs,
        scope = "Every final-plugin method/field instruction and type reference; native declarations plus exact or reference-shared generic bodies. Interface/abstract callvirt validates the preserved dispatch declaration, not a particular runtime implementation.",
        counts = findings.Values.GroupBy(x=>x.status).ToDictionary(g=>g.Key,g=>g.Count()),
        findings = findings.Values.Where(x=>x.status!="interpreted").ToArray(),
        errors = findings.Values.Where(x=>x.status=="error").Select(x=>x.kind+": "+x.operand+" — "+x.reason).ToArray()
    }; } }
    public IEnumerable<string> Errors { get { FinishTypeProofs(); return findings.Values.Where(x=>x.status=="error").Select(x=>"Windows native "+x.kind+": "+x.operand+" — "+x.reason); } }
    public void Dispose() { foreach(var m in native.Values)m.Dispose(); manifestDocument.Dispose(); scriptDocument.Dispose(); }
}
