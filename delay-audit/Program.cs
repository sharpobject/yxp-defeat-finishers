using ILRuntime.Mono.Cecil;
using ILRuntime.Mono.Cecil.Cil;
using System.Text.Json;

var module = ModuleDefinition.ReadModule(args[0]);
if(args.Length>2 && args[2]=="signatures")
{
    foreach(var type in module.Types.Where(t=>t.Name is "CharacterBattleAnimator" or "CharacterBattleAnimatorPool" or "BattleExecuter" or "BattleManager"))
    foreach(var method in type.Methods.Where(m=>m.Name is "PlayAnimation" or "AddAnimation" or "WaitForEvent" or "WaitForEnd" or "Despawn" or "ResetInstance" or "Execute" or "SetVisible" or "OnDestroy" or "ChangeSceneType"))
        Console.WriteLine(method.FullName);
    foreach(var type in All(module.Types))
    foreach(var method in type.Methods.Where(m=>m.HasBody))
    foreach(var ins in method.Body.Instructions)
        if(ins.Operand is MethodReference call && call.DeclaringType.Name=="CharacterBattleAnimator" && call.Name=="PlayAnimation" && call.Parameters[1].ParameterType.FullName=="System.String")
            Console.WriteLine("String animation caller: "+method.FullName);
}
var sites = new List<Site>();
foreach (var type in module.Types.Where(t => t.Name == "CardActionBase" || t.Name.StartsWith("Card_", StringComparison.Ordinal) ||
    t.Name is "BattleCharacter" or "BattleExecuter" or "FateStrategyFunctions" or "XianMoStrategyFunctions" or "KeYinCardFunctions"))
foreach (var entry in type.Methods)
{
    if ((type.Name is "BattleExecuter" or "BattleCharacter") && entry.IsStatic) continue;
    if (type.Name == "BattleCharacter" && entry.Name is "InitData" or "OnBattleStarted" or "OnBattleWin" or "OnBattleLose") continue;
    if (type.Name == "BattleExecuter" && entry.Name is "BeforeBattleStarted" or "LoadBattleSceneAsync") continue;
    if (type.Name is "FateStrategyFunctions" or "XianMoStrategyFunctions" && entry.Name == "OnBattleStart") continue;
    if (type.Name == "KeYinCardFunctions" && entry.Name is "OnBattleStartKeYin" or "OnBattleStartKeYinCardFunction") continue;
    var ownerType = type.Name is "BattleExecuter" or "BattleCharacter" ? type.Name : null;
    var parameter = ownerType == null ? entry.Parameters.FirstOrDefault(p => p.ParameterType.FullName == "BattleCharacter") : null;
    if (ownerType == null && parameter == null) continue;
    ownerType ??= "BattleCharacter";
    var method = entry;
    var asyncAttr = entry.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
    if (asyncAttr != null)
    {
        var state = ((TypeReference)asyncAttr.ConstructorArguments[0].Value).Resolve();
        method = state.Methods.Single(m => m.Name == "MoveNext");
        if (!state.Fields.Any(f => f.Name == (parameter == null ? "<>4__this" : parameter.Name) && f.FieldType.FullName == ownerType)) continue;
    }
    if (!method.HasBody) continue;
    foreach (var ins in method.Body.Instructions)
    {
        if (ins.Operand is not MethodReference call || call.DeclaringType.FullName != "Cysharp.Threading.Tasks.UniTask" || call.Name != "Delay" ||
            call.Parameters.Count != 5 || call.Parameters[0].ParameterType.FullName != "System.Int32" || call.Parameters[1].ParameterType.FullName != "System.Boolean") continue;
        var producer = Producer(ins.Previous, 5);
        sites.Add(new Site(entry, method, ins, producer));
    }
}
var groups = sites.GroupBy(s=>s.Method).ToArray();
var rows = new List<object>();
int safeSites=0, collisionSites=0, dynamicSites=0;
foreach(var group in groups)
{
    var method=group.Key;
    var roots=group.Select(s=>s.Producer).Where(i=>i!=null).ToHashSet();
    foreach(var site in group)
    {
        var value=Constant(site.Producer);
        var collisions=value==null ? Array.Empty<Instruction>() : method.Body.Instructions.Where(i=>Constant(i)==value && !roots.Contains(i)).ToArray();
        var classification=value==null ? "dynamic" : collisions.Length==0 ? "safe-literal" : "literal-collision";
        if(classification=="dynamic")dynamicSites++;else if(classification=="safe-literal")safeSites++;else collisionSites++;
        rows.Add(new {entry=site.Entry.FullName,method=method.FullName,offset=site.Call.Offset,classification,value,
            producer=site.Producer?.ToString(),collisions=collisions.Select(i=>new{instruction=i.ToString(),previous=i.Previous?.ToString(),next=i.Next?.ToString()}).ToArray(),
            preceding=method.Body.Instructions.Skip(Math.Max(0,method.Body.Instructions.IndexOf(site.Call)-13)).Take(14).Select(i=>i.ToString()).ToArray()});
    }
}
File.WriteAllText(args[1],JsonSerializer.Serialize(new {total=sites.Count,methods=groups.Length,safeSites,collisionSites,dynamicSites,rows},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Total {sites.Count}; methods {groups.Length}; literal-only-safe {safeSites}; literal-collision {collisionSites}; dynamic {dynamicSites}");

// The authored loader adapter keeps the20 colliding passive/text waits intact.
// Only admitted presentation literals generate SDK registration calls.
var registration=new List<string>();
foreach(var group in groups)
{
    var entry=group.First().Entry;
    if(entry.DeclaringType.Name=="BattleCharacter" && entry.Name=="Attack")continue;
    if(entry.ReturnType.FullName=="System.Void" && entry.Name.StartsWith("<",StringComparison.Ordinal))
    {
        var body=group.Key.Body.Instructions;
        var results=body.Where(i=>i.Operand is MethodReference m && m.Name=="GetResult" &&
            m.DeclaringType.FullName=="Cysharp.Threading.Tasks.UniTask/Awaiter").ToArray();
        if(group.Count()!=1 || results.Length!=1 || body.Skip(body.IndexOf(results[0])+1).Any(i=>
            i.Operand is MethodReference m && !(m.DeclaringType.FullName=="System.Runtime.CompilerServices.AsyncVoidMethodBuilder" &&
                m.Name is "SetResult" or "SetException")))
            throw new InvalidDataException("Unawaited callback has work after its native delay: "+entry.FullName);
        Console.WriteLine("Omitted terminal unawaited delay: "+entry.FullName);
        continue;
    }
    var roots=group.Select(s=>s.Producer).Where(i=>i!=null).ToHashSet();
    foreach(int value in group.Select(s=>Constant(s.Producer)).Where(v=>v.HasValue).Select(v=>v!.Value).Distinct().Order())
    {
        if(entry.DeclaringType.Name=="BattleExecuter" && entry.Name=="Execute" && value is 1500 or 1800)continue;
        if(group.Key.Body.Instructions.Any(i=>Constant(i)==value&&!roots.Contains(i)))continue;
        var target=Yx.Shared.SaturateRewriter.StateMachineMoveNext(entry)??entry;
        if(target!=group.Key)throw new InvalidDataException("SDK cannot locate admitted native delay body: "+entry.FullName);
        var same=entry.DeclaringType.Methods.Where(m=>m.Name==entry.Name&&m.Parameters.Count==entry.Parameters.Count).ToArray();
        if(same.Length!=1)throw new InvalidDataException("Ambiguous SDK constant method "+entry.FullName);
        registration.Add($"            result[{registration.Count}] = hooks.OverrideConstant(\"{entry.DeclaringType.FullName}\", \"{entry.Name}\", {entry.Parameters.Count}, {value}, null);");
    }
}
string output=Path.Combine(Path.GetDirectoryName(args[1])!,"..","generated","FinisherDelays.cs");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output,"// Generated from exact native Delay argument producers by delay-audit.\nusing System;\nusing Yx.ModSdk.Hooks;\nnamespace YxpDefeatFinishers {\n    internal static class FinisherDelays {\n        internal static ConstOverride[] Install(ModHooks hooks) {\n            var result = new ConstOverride["+registration.Count+"];\n"+string.Join("\n",registration)+"\n            for (int i=0;i<result.Length;i++) if (!result[i].Applied) throw new InvalidOperationException(\"Required native presentation delay unavailable: \" + result[i].Report.Describe());\n            return result;\n        }\n    }\n}\n");
Console.WriteLine("Generated supported delay registrations: "+registration.Count);
if(args.Length>2 && args[2]=="generate-only") return;

// Exercise the SDK's actual rewriter at build time. No copy of its algorithm.
var get = new MethodReference("Get",module.TypeSystem.Int32,
    new TypeReference("Yx.ModSdk.Hooks","ConstOverrides",module,module.TypeSystem.CoreLibrary));
get.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
get.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
var expected = new[]{("BattleExecuter","Execute",100,"firstCharacter",2,3),
    ("BattleExecuter","Execute",500,"firstCharacter",2,3),
    ("CardActionBase","OnBeforeExecuted",200,"src",3,14)};
int slot=0;
foreach(var (owner,name,value,hint,expectedIdentity,expectedDelay) in expected)
{
    var group=groups.Single(g=>g.First().Entry.DeclaringType.Name==owner && g.First().Entry.Name==name);
    var method=group.Key;
    var delayRoots=group.Where(s=>Constant(s.Producer)==value).Select(s=>s.Producer!).ToHashSet();
    var original=method.Body.Instructions.Where(i=>Constant(i)==value).ToArray();
    int identitySlot=slot++,delaySlot=slot++;
    int identity=Yx.Shared.ConstRewriter.Apply(method,value,hint,identitySlot,get);
    int delay=Yx.Shared.ConstRewriter.Apply(method,value,null,delaySlot,get);
    if(identity!=expectedIdentity || delay!=expectedDelay)
        throw new InvalidDataException($"SDK override set mismatch {owner}.{name}:{value}: {identity}/{delay}");
    foreach(var ins in original)
    {
        int wanted=delayRoots.Contains(ins)?delaySlot:identitySlot;
        if(Constant(ins.Next)!=wanted || ins.Next.Next?.Operand!=get)
            throw new InvalidDataException($"SDK changed wrong literal {owner}.{name}:{ins.Offset}");
    }
    // Re-registering either selector must not stack another lookup on literals.
    if(Yx.Shared.ConstRewriter.Apply(method,value,hint,identitySlot,get)!=0 ||
        Yx.Shared.ConstRewriter.Apply(method,value,null,delaySlot,get)!=0)
        throw new InvalidDataException("SDK constant registration is not idempotent");
    Console.WriteLine($"Actual SDK verified {owner}.{name} value{value}: identity {identity}; delay-only {delay}; repeat 0");
}

static int? Constant(Instruction? i)=>i?.OpCode.Code switch {
 Code.Ldc_I4_M1=>-1,Code.Ldc_I4_0=>0,Code.Ldc_I4_1=>1,Code.Ldc_I4_2=>2,Code.Ldc_I4_3=>3,Code.Ldc_I4_4=>4,
 Code.Ldc_I4_5=>5,Code.Ldc_I4_6=>6,Code.Ldc_I4_7=>7,Code.Ldc_I4_8=>8,Code.Ldc_I4=>Convert.ToInt32(i.Operand),Code.Ldc_I4_S=>Convert.ToInt32(i.Operand),_=>null};
static Instruction? Producer(Instruction? i,int need)
{
    for(;i!=null;i=i.Previous)
    {
        int push=Push(i),pop=Pop(i);
        if(push<0||pop<0)return null;
        if(push>=need)return i;
        need=need-push+pop;
    }
    return null;
}
static int Push(Instruction i)
{
    if(i.OpCode.Code==Code.Newobj)return 1;
    if(i.OpCode.StackBehaviourPush==StackBehaviour.Varpush)
        return i.Operand is MethodReference m?(m.ReturnType.FullName=="System.Void"?0:1):-1;
    return i.OpCode.StackBehaviourPush switch {StackBehaviour.Push0=>0,StackBehaviour.Push1_push1=>2,_=>1};
}
static int Pop(Instruction i)
{
    if(i.OpCode.StackBehaviourPop==StackBehaviour.Varpop)
        return i.Operand is MethodReference m?m.Parameters.Count+(m.HasThis&&i.OpCode.Code!=Code.Newobj?1:0):-1;
    return i.OpCode.StackBehaviourPop switch {
        StackBehaviour.Pop0=>0,StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref=>1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi=>2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref=>3,_=>-1};
}
static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types)
{
    foreach(var type in types) { yield return type; foreach(var child in All(type.NestedTypes))yield return child; }
}
record Site(MethodDefinition Entry,MethodDefinition Method,Instruction Call,Instruction? Producer);
