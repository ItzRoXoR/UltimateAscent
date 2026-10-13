// Static check of every Harmony patch in UltimateAscent.dll against the installed game.
// For each patch class it resolves the target method in Assembly-CSharp (or another referenced
// assembly), reports missing/ambiguous targets, and verifies that every parameter Harmony has
// to inject (by name) exists on the target. Usage:
//   dotnet run --project tools/PatchCheck -- <UltimateAscent.dll> <PEAK_Data/Managed> [extra assembly dirs...]
using Mono.Cecil;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: PatchCheck <plugin.dll> <managed dir> [more dirs]");
    return 2;
}

var resolver = new DefaultAssemblyResolver();
foreach (string dir in args.Skip(1))
{
    resolver.AddSearchDirectory(dir);
}
var plugin = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });

int checkedPatches = 0, problems = 0, dynamicTargets = 0;
var specialPrefixes = new[] { "__instance", "__result", "__state", "__exception", "__originalMethod", "__runOriginal", "__args", "___" };

foreach (TypeDefinition type in plugin.MainModule.GetTypes())
{
    var patchAttributes = type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToList();
    if (patchAttributes.Count == 0)
    {
        continue;
    }
    checkedPatches++;
    bool dynamicTarget = type.Methods.Any(m => m.Name is "TargetMethod" or "TargetMethods");

    TypeReference targetType = null;
    string methodName = null;
    TypeReference[] argumentTypes = null;
    foreach (CustomAttribute attribute in patchAttributes)
    {
        foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
        {
            switch (argument.Value)
            {
                case TypeReference t:
                    targetType = t;
                    break;
                case string s:
                    methodName = s;
                    break;
                case CustomAttributeArgument[] array when array.All(x => x.Value is TypeReference):
                    argumentTypes = array.Select(x => (TypeReference)x.Value).ToArray();
                    break;
            }
        }
    }

    if (dynamicTarget)
    {
        dynamicTargets++;
        Console.WriteLine($"  dynamic  {type.FullName} (target chosen at runtime)");
        continue;
    }
    if (targetType == null || methodName == null)
    {
        Console.WriteLine($"PROBLEM   {type.FullName}: incomplete [HarmonyPatch] (type={targetType?.FullName}, method={methodName})");
        problems++;
        continue;
    }
    TypeDefinition targetDefinition;
    try
    {
        targetDefinition = targetType.Resolve();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"PROBLEM   {type.FullName}: cannot resolve {targetType.FullName}: {ex.Message}");
        problems++;
        continue;
    }
    if (targetDefinition == null)
    {
        Console.WriteLine($"PROBLEM   {type.FullName}: type {targetType.FullName} not found");
        problems++;
        continue;
    }
    var candidates = targetDefinition.Methods.Where(m => m.Name == methodName).ToList();
    if (argumentTypes != null)
    {
        candidates = candidates.Where(m => m.Parameters.Count == argumentTypes.Length &&
                                           m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(argumentTypes.Select(a => a.FullName))).ToList();
    }
    if (candidates.Count == 0)
    {
        Console.WriteLine($"PROBLEM   {type.FullName}: {targetType.Name}.{methodName} not found");
        problems++;
        continue;
    }
    if (candidates.Count > 1)
    {
        Console.WriteLine($"PROBLEM   {type.FullName}: {targetType.Name}.{methodName} is ambiguous ({candidates.Count} overloads); add argument types");
        problems++;
        continue;
    }
    MethodDefinition target = candidates[0];
    var targetParameters = target.Parameters.Select(p => p.Name).ToHashSet();
    foreach (MethodDefinition patchMethod in type.Methods.Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer"))
    {
        foreach (ParameterDefinition parameter in patchMethod.Parameters)
        {
            string name = parameter.Name;
            if (specialPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                if (name == "__instance" && target.IsStatic)
                {
                    Console.WriteLine($"PROBLEM   {type.FullName}.{patchMethod.Name}: __instance on static {targetType.Name}.{methodName}");
                    problems++;
                }
                if (name == "__result" && target.ReturnType.FullName == "System.Void")
                {
                    Console.WriteLine($"PROBLEM   {type.FullName}.{patchMethod.Name}: __result on void {targetType.Name}.{methodName}");
                    problems++;
                }
                if (name.StartsWith("___", StringComparison.Ordinal))
                {
                    string field = name.Substring(3);
                    if (targetDefinition.Fields.All(f => f.Name != field))
                    {
                        Console.WriteLine($"PROBLEM   {type.FullName}.{patchMethod.Name}: field {field} not on {targetType.Name}");
                        problems++;
                    }
                }
                continue;
            }
            if (!targetParameters.Contains(name))
            {
                Console.WriteLine($"PROBLEM   {type.FullName}.{patchMethod.Name}: parameter '{name}' does not exist on {targetType.Name}.{methodName}({string.Join(", ", target.Parameters.Select(p => p.ParameterType.Name + " " + p.Name))})");
                problems++;
            }
        }
    }
    Console.WriteLine($"  ok       {targetType.Name}.{methodName}  <- {type.Name}");
}

Console.WriteLine();
Console.WriteLine($"{checkedPatches} patch classes, {dynamicTargets} with runtime targets, {problems} problem(s).");
return problems == 0 ? 0 : 1;
