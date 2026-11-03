// Patches the game's assembly so a race fills screens taller than 16:9.
//
// Each edit swaps a 16:9 constant or a call in one named method for a call into
// SlipstreamAspect.dll, which has to sit beside the patched assembly. An edit names how
// many sites it expects; any other count writes nothing, so an unknown build of the game
// runs letterboxed rather than half-patched. A patched assembly references the helper,
// which is how a second run recognises it.
//
//     dotnet aspect-patch.dll slipstream.dll
//
// Exit 0: patched, or patched already. Exit 1: the file is unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: aspect-patch.dll slipstream.dll");
            return 1;
        }
        string path = args[0];
        try
        {
            // Writing resolves the game's references: FNA beside it, the rest in this runtime.
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            resolver.AddSearchDirectory(RuntimeEnvironment.GetRuntimeDirectory());
            var parameters = new ReaderParameters { InMemory = true, AssemblyResolver = resolver };
            using ModuleDefinition module = ModuleDefinition.ReadModule(path, parameters);
            if (module.AssemblyReferences.Any(r => r.Name == Patch.Helper))
            {
                Console.WriteLine($"{path}: patched already");
                return 0;
            }
            int sites = new Patch(module).Apply();
            string temporary = path + ".tmp";
            module.Write(temporary);
            File.Move(temporary, path, true);
            Console.WriteLine($"{path}: {sites} sites patched");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"{path}: not patched: {e.Message}");
            return 1;
        }
    }
}

/// <summary>The edits to one module; Apply makes them all or throws before the module is
/// written.</summary>
internal sealed class Patch
{
    public const string Helper = "SlipstreamAspect";
    private const string Canvas = "Slipstream.Core.Canvas";
    private const string Road = "Slipstream.Elements.Road";
    private const string Hud = "Slipstream.Elements.HUD";
    private const string Gameplay = "Slipstream.Screens.Gameplay";
    private const string Background = "Slipstream.Elements.Background";
    private const string Texture2D = "Microsoft.Xna.Framework.Graphics.Texture2D";
    private const string Rectangle = "Microsoft.Xna.Framework.Rectangle";
    private const int BaseHeight = 270;
    private const int HalfBaseHeight = BaseHeight / 2;
    // Background.DrawScene's height argument.
    private const int SceneHeightParameter = 6;

    private readonly ModuleDefinition module;
    private readonly TypeReference aspect;
    private readonly HashSet<MethodDefinition> touched = new();
    private int sites;

    public Patch(ModuleDefinition module)
    {
        this.module = module;
        var helper = new AssemblyNameReference(Helper, new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(helper);
        aspect = new TypeReference("SlipstreamPort", "Aspect", module, helper);
    }

    /// <summary>Makes every edit; returns the number of sites changed.</summary>
    public int Apply()
    {
        TypeSystem types = module.TypeSystem;
        TypeReference texture = module.GetTypeReferences().Single(t => t.FullName == Texture2D);
        TypeReference fnaRectangle = module.GetTypeReferences().Single(t => t.FullName == Rectangle);
        // A fresh reference: one read from metadata may not know it is a value type yet.
        var rectangle = new TypeReference(fnaRectangle.Namespace, fnaRectangle.Name, module, fnaRectangle.Scope, true);
        MethodReference height = Call("get_Height", types.Int32);
        MethodReference heightF = Call("get_HeightF", types.Single);
        MethodReference halfHeight = Call("get_HalfHeight", types.Int32);
        MethodReference roadHeight = Call("get_RoadHeight", types.Int32);
        MethodReference configure = Call("Configure", types.Void);
        MethodReference band = Call("Band", types.Void);
        MethodReference full = Call("Full", types.Void);
        MethodReference overlayArea = Call("OverlayArea", types.Void, types.Int32, types.Int32, types.Int32, types.Int32);
        MethodReference drawFull = Call("DrawFull", types.Void, texture, types.Single, types.Single, types.Single, types.Single, types.Single, types.Single);
        MethodReference hudFull = Call("HudFull", types.Void, Type(Hud), types.Single, types.Single);
        MethodReference setRoadScreenSize = Call("SetRoadScreenSize", types.Void, Type(Road), types.Boolean, types.Boolean);
        MethodReference fovHeight = Call("FovHeight", types.Int32, Type(Road));
        MethodReference viewBounds = Call("ViewBounds", rectangle, types.Int32, types.Int32, types.Int32, types.Int32);

        MethodDefinition setup = Method(Canvas, "SetupRenderTargets");
        Prepend(setup, configure);
        Swap(Find(setup, IsFloat, 1), heightF);
        Swap(Find(setup, IsInt(BaseHeight), 1), height);
        Swap(Find(Method(Canvas, "UpdateMatrices"), IsFloat, 1), heightF);
        Swap(Find(Method(Canvas, "SetDrawArea"), IsFloat, 3), heightF);
        Swap(Find(Method(Canvas, "PostProcess"), IsInt(BaseHeight), 1), height);
        MethodDefinition end = Method(Canvas, "End");
        Prepend(end, full);
        Swap(Find(end, IsFloat, 2), heightF);
        AfterBody(Method(Canvas, "Begin"), band);

        Swap(Find(Method(Road, "SetScreenSize"), IsInt(BaseHeight), 1), roadHeight);
        Swap(Find(Method(Road, "SetFOV"), i => IsField(i, Road, "ScreenHeight"), 2), fovHeight);

        MethodDefinition setState = Method(Gameplay, "SetState");
        Swap(Find(setState, i => IsCall(i, Road, "SetScreenSize"), 1), setRoadScreenSize);
        Swap(Find(setState, IsInt(BaseHeight), 1), height);
        MethodDefinition players = Method(Gameplay, "InitializePlayers");
        Swap(Find(players, IsInt(BaseHeight), 1), height);
        Swap(Find(players, IsInt(HalfBaseHeight), 3), halfHeight);
        Swap(Find(players, i => i.OpCode == OpCodes.Newobj && ((MethodReference)i.Operand).DeclaringType.FullName == Rectangle
            && i.Next.OpCode == OpCodes.Stfld && ((FieldReference)i.Next.Operand).Name == "ScreenBounds", 1), viewBounds);
        MethodDefinition render = Method(Gameplay, "Render");
        Swap(Find(render, i => IsCall(i, Canvas, "SetDrawArea") && IsInt(BaseHeight)(i.Previous), 1), overlayArea);
        Swap(Find(render, IsFloat, 1).Select(i => NextCall(i, Canvas, "Draw")), drawFull);
        Swap(Find(render, i => i.OpCode == OpCodes.Ldfld && ((FieldReference)i.Operand).Name == "MetaHUD", 1)
            .Select(i => NextCall(i, Hud, "Draw")), hudFull);

        MethodDefinition scene = Method(Background, "DrawScene");
        ParameterDefinition sceneHeight = scene.Parameters[SceneHeightParameter];
        if (sceneHeight.ParameterType.MetadataType != MetadataType.Int32)
        {
            throw new InvalidDataException($"{Background}.DrawScene: argument {SceneHeightParameter} is not the height");
        }
        foreach (Instruction site in Find(scene, IsInt(BaseHeight), 1))
        {
            site.OpCode = OpCodes.Ldarg;
            site.Operand = sceneHeight;
            sites++;
        }

        foreach (MethodDefinition method in touched)
        {
            method.Body.OptimizeMacros();
        }
        return sites;
    }

    private static bool IsFloat(Instruction i) => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == BaseHeight;

    private static Func<Instruction, bool> IsInt(int value) => i => i != null && i.OpCode == OpCodes.Ldc_I4 && (int)i.Operand == value;

    private static bool IsCall(Instruction i, string type, string name) =>
        (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
        && i.Operand is MethodReference m && m.Name == name && m.DeclaringType.FullName == type;

    private static bool IsField(Instruction i, string type, string name) =>
        i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == name && f.DeclaringType.FullName == type;

    /// <summary>Finds the first call after an instruction; returns it if it calls
    /// type.name, throws otherwise.</summary>
    private static Instruction NextCall(Instruction from, string type, string name)
    {
        for (Instruction i = from.Next; i != null; i = i.Next)
        {
            if (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Newobj)
            {
                return IsCall(i, type, name) ? i : throw new InvalidDataException($"expected a call to {type}.{name}, found {i}");
            }
        }
        throw new InvalidDataException($"no call to {type}.{name}");
    }

    private TypeDefinition Type(string name) =>
        module.GetType(name) ?? throw new InvalidDataException($"no type {name}");

    /// <summary>Looks up a method by name, which has to be unique in its type; returns it
    /// with its macros expanded, so instructions compare in their long forms.</summary>
    private MethodDefinition Method(string type, string name)
    {
        MethodDefinition[] found = Type(type).Methods.Where(m => m.Name == name && m.HasBody).ToArray();
        if (found.Length != 1)
        {
            throw new InvalidDataException($"{type}.{name}: {found.Length} methods");
        }
        if (touched.Add(found[0]))
        {
            found[0].Body.SimplifyMacros();
        }
        return found[0];
    }

    private MethodReference Call(string name, TypeReference returns, params TypeReference[] parameters)
    {
        var method = new MethodReference(name, returns, aspect) { HasThis = false };
        foreach (TypeReference parameter in parameters)
        {
            method.Parameters.Add(new ParameterDefinition(parameter));
        }
        return method;
    }

    /// <summary>Finds the instructions that match; returns them, or throws unless there
    /// are exactly as many as expected.</summary>
    private static List<Instruction> Find(MethodDefinition method, Func<Instruction, bool> match, int expected)
    {
        List<Instruction> found = method.Body.Instructions.Where(match).ToList();
        if (found.Count != expected)
        {
            throw new InvalidDataException($"{method.FullName}: {found.Count} sites, expected {expected}");
        }
        return found;
    }

    /// <summary>Turns each instruction into a static call; it keeps its place, so branches
    /// into it still land.</summary>
    private void Swap(IEnumerable<Instruction> instructions, MethodReference call)
    {
        foreach (Instruction site in instructions)
        {
            site.OpCode = OpCodes.Call;
            site.Operand = call;
            sites++;
        }
    }

    private void Prepend(MethodDefinition method, MethodReference call)
    {
        ILProcessor il = method.Body.GetILProcessor();
        il.InsertBefore(method.Body.Instructions[0], il.Create(OpCodes.Call, call));
        sites++;
    }

    /// <summary>Runs a call on the method's one way out: the ret becomes the call and a new
    /// ret follows it, so branches to the old ret pass through the call.</summary>
    private void AfterBody(MethodDefinition method, MethodReference call)
    {
        Instruction ret = Find(method, i => i.OpCode == OpCodes.Ret, 1)[0];
        ret.OpCode = OpCodes.Call;
        ret.Operand = call;
        method.Body.GetILProcessor().InsertAfter(ret, Instruction.Create(OpCodes.Ret));
        sites++;
    }
}
