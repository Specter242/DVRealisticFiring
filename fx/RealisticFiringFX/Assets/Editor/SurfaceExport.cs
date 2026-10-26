using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RealisticFiringFX
{
    /// <summary>
    /// Writes out the code Unity generates for the mod's surface shaders (the
    /// inspector's "Show generated code"), the source of their vertex and
    /// fragment versions. Those carry a single-pass stereo variant on the
    /// mod's own keyword: Unity strips its built-in stereo variants from a
    /// bundle built without VR, and a surface shader's own code comes too
    /// late in its passes to switch stereo on. Run it after changing a
    /// surface shader in Surface/, then carry the change over:
    /// -executeMethod RealisticFiringFX.SurfaceExport.ExportFromCommandLine -outputPath DIR
    /// </summary>
    public static class SurfaceExport
    {
        private static readonly string[] Shaders = { "RealisticFiring/CoalBedSurface", "RealisticFiring/FireIronSurface", "RealisticFiring/FireboxWallSurface", "RealisticFiring/CoalLumpsSurface" };

        public static void ExportFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var output = "Temp/SurfaceExport";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-outputPath")
                    output = args[i + 1];
            }
            Directory.CreateDirectory(output);
            var open = typeof(ShaderUtil).GetMethod("OpenParsedSurfaceShader", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (open == null)
                throw new MissingMethodException("ShaderUtil.OpenParsedSurfaceShader");
            // It opens the file in the script editor too: open it in nothing.
            const string editorKey = "kScriptsDefaultApp";
            var editor = EditorPrefs.GetString(editorKey, "");
            EditorPrefs.SetString(editorKey, "/bin/true");
            try
            {
                foreach (var name in Shaders)
                {
                    var shader = Shader.Find(name);
                    if (shader == null)
                        throw new FileNotFoundException("No shader " + name);
                    // Only this shader's file left to find.
                    foreach (var old in new DirectoryInfo("Temp").GetFiles("GeneratedFromSurface-*.shader"))
                        old.Delete();
                    open.Invoke(null, new object[] { shader });
                    var generated = new DirectoryInfo("Temp").GetFiles("GeneratedFromSurface-*.shader").SingleOrDefault();
                    if (generated == null)
                        throw new FileNotFoundException("Unity wrote no generated code for " + name);
                    var target = Path.Combine(output, name.Replace('/', '-') + ".generated.shader");
                    File.Copy(generated.FullName, target, true);
                    Debug.Log("Realistic Firing FX: generated code for " + name + " -> " + target);
                }
            }
            finally
            {
                EditorPrefs.SetString(editorKey, editor);
            }
        }
    }
}
