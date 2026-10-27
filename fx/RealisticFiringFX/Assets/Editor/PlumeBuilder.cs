using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RealisticFiringFX
{
    /// <summary>
    /// Builds the chimney plume prefab from code and packs it into the asset
    /// bundle the Realistic Firing mod loads. The mod emits every particle
    /// itself (velocity, size, and color per particle) and moves them each
    /// frame, so these systems only carry the look: materials, flipbook
    /// animation, growth, and fade. Defining them here keeps every setting
    /// reviewable in Git instead of in a prefab's serialized YAML.
    /// </summary>
    public static class PlumeBuilder
    {
        public const string BundleName = "realisticfiring_fx";

        // Realistic Stack Effects' bundle: the plume alone, from the same assets.
        public const string StackBundleName = "realisticstackeffects_fx";
        public const string PrefabName = "ChimneyPlumeFX";
        public const string OverflowPrefabName = "InjectorOverflowFX";
        public const string FireboxPrefabName = "FireboxFX";

        private const string Root = "Assets/RealisticFiring";
        private const string TexturesDir = Root + "/Textures";
        private const string MaterialsDir = Root + "/Materials";
        private const string FontsDir = Root + "/Fonts";
        private const string PrefabPath = Root + "/" + PrefabName + ".prefab";
        private const string OverflowPrefabPath = Root + "/" + OverflowPrefabName + ".prefab";
        private const string FireboxPrefabPath = Root + "/" + FireboxPrefabName + ".prefab";
        private const string ShaderName = "RealisticFiring/ChimneySmoke";
        private const string SparkShaderName = "RealisticFiring/Spark";
        private const string CoalBedShaderName = "RealisticFiring/CoalBed";
        private const string FlameShaderName = "RealisticFiring/Flame";
        private const string FireboxSmokeShaderName = "RealisticFiring/FireboxSmoke";
        private const string EmberBedShaderName = "RealisticFiring/EmberBed";
        private const string FlameCardShaderName = "RealisticFiring/FlameCard";
        private const string FireIronShaderName = "RealisticFiring/FireIron";
        private const string HeatHazeShaderName = "RealisticFiring/HeatHaze";
        public const string FireIronMaterialName = "FireIron";
        private const string FireboxWallShaderName = "RealisticFiring/FireboxWall";
        public const string FireboxWallMaterialName = "FireboxWall";
        private const string FireboxVolumeShaderName = "RealisticFiring/FireboxVolume";
        public const string FireboxVolumeMaterialName = "FireboxVolume";
        private const int FireVolumeNoiseSize = 64;
        private const string CoalLumpsShaderName = "RealisticFiring/CoalLumps";
        public const string CoalLumpsMaterialName = "FireboxCoalLumps";
        private const string PlumeCompositeShaderName = "RealisticFiring/PlumeComposite";
        public const string PlumeCompositeMaterialName = "PlumeComposite";
        private const string PlumeDepthShaderName = "RealisticFiring/PlumeDepth";
        public const string PlumeDepthMaterialName = "PlumeDepth";

        [MenuItem("Realistic Firing/Build plume bundle")]
        public static void BuildFromMenu() => Build("Build/AssetBundles");

        /// <summary>
        /// Entry point for batch mode: -executeMethod
        /// RealisticFiringFX.PlumeBuilder.BuildFromCommandLine -outputPath DIR
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var output = "Build/AssetBundles";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-outputPath")
                    output = args[i + 1];
            }
            Build(output);
        }

        private static void Build(string output)
        {
            AssetDatabase.Refresh();
            var shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException($"Shader '{ShaderName}' did not compile; see the editor log");

            var sparkShader = Shader.Find(SparkShaderName);
            if (sparkShader == null)
                throw new InvalidOperationException($"Shader '{SparkShaderName}' did not compile; see the editor log");

            // The chimney's smoke: wisps, eight of them a row each, fraying as
            // they thin (the firebox's smoke keeps the round puffs).
            // Their RGB is their surface (the normal and thickness the shader
            // lights), so it is read as data, not color.
            var puff = CreateMaterial(shader, "Puff", ImportFlipbook("smoke_wisp_flipbook.png", data: true));
            puff.SetFloat("_Fray", 0.5f);
            puff.SetFloat("_Surface", 1f);
            puff.SetFloat("_Tiles", 8f);
            // Round billows where the steam is thick, blended to the wisps as it thins.
            puff.SetTexture("_BillowTex", ImportFlipbook("smoke_billow_flipbook.png", data: true));
            // Rolling steam: puffs simulated in 3D, rolling in a loop, lit
            // six ways (fx/textures/make_rolling_steam.py). The mod picks
            // each particle's puff and frame (its second custom data).
            puff.SetFloat("_SixWay", 1f);
            puff.SetTexture("_LightA", ImportFlipbook("smoke_roll_a.png", data: true));
            puff.SetTexture("_LightB", ImportFlipbook("smoke_roll_b.png", data: true));
            puff.SetFloat("_SixColumns", 16f);
            puff.SetFloat("_SixRows", 16f);
            puff.SetFloat("_SixFrames", 32f);
            // As a particle thins it turns to a wisp: the wisp flipbook above,
            // as the plume drew before its puffs rolled.
            puff.SetFloat("_Wisps", 1f);
            // The same wisps with their relief baked in, as 1.13 drew them.
            puff.SetTexture("_WispTex", ImportFlipbook("smoke_wisp_baked.png"));
            EditorUtility.SetDirty(puff);
            var jet = CreateMaterial(shader, "Jet", ImportFlipbook("smoke_jet_flipbook.png", data: true));
            jet.SetFloat("_Surface", 1f);
            jet.SetFloat("_Tiles", 4f);
            EditorUtility.SetDirty(jet);
            var spark = CreateMaterial(sparkShader, "Spark", ImportFlipbook("spark.png"));
            var droplet = CreateMaterial(shader, "Droplet", ImportFlipbook("spark.png"));
            // The hot gas's shimmer: the scene behind, bent by the fire's noise.
            var heat = new Material(FindShader(HeatHazeShaderName)) { name = "SmokeHeat" };
            heat.SetTexture("_NoiseTex", ImportData("fire_noise.png"));
            SaveMaterial(heat);
            CreatePrefab(puff, jet, spark, heat);
            CreateOverflowPrefab(puff, droplet);

            var coalBed = new Material(FindShader(CoalBedShaderName)) { name = "FireboxCoalBed" };
            coalBed.SetTexture("_CoalTex", ImportData("coal_bed.png"));
            coalBed.SetTexture("_NormalTex", ImportNormal("coal_normal.png"));
            coalBed.SetTexture("_NoiseTex", ImportData("fire_noise.png"));
            SaveMaterial(coalBed);
            var flame = CreateMaterial(FindShader(FlameShaderName), "Flame", ImportFlipbook("flame_flipbook.png"));
            flame.SetFloat("_Intensity", 3.5f);
            var fireboxSmoke = CreateMaterial(FindShader(FireboxSmokeShaderName), "FireboxSmoke", ImportFlipbook("smoke_puff_flipbook.png"));
            fireboxSmoke.SetFloat("_InvFade", 6f);
            var stokerJet = new Material(FindShader("RealisticFiring/StokerJet")) { name = "StokerJet" };
            stokerJet.SetTexture("_NoiseTex", coalBed.GetTexture("_NoiseTex"));
            SaveMaterial(stokerJet);
            AssetImporter.GetAtPath($"{MaterialsDir}/StokerJet.mat").SetAssetBundleNameAndVariant(BundleName, "");
            var embers = new Material(FindShader(EmberBedShaderName)) { name = "FireboxEmbers" };
            embers.SetTexture("_CoalTex", coalBed.GetTexture("_CoalTex"));
            embers.SetTexture("_NoiseTex", coalBed.GetTexture("_NoiseTex"));
            SaveMaterial(embers);
            var flameCards = new Material(FindShader(FlameCardShaderName)) { name = "FireboxFlameCards" };
            flameCards.SetTexture("_NoiseTex", coalBed.GetTexture("_NoiseTex"));
            SaveMaterial(flameCards);
            CreateFireboxPrefab(coalBed, flame, fireboxSmoke, embers, flameCards);

            // The fire iron's look; the mod builds its mesh.
            var fireIron = new Material(FindShader(FireIronShaderName)) { name = FireIronMaterialName };
            fireIron.SetTexture("_MainTex", ImportAlbedo("fire_iron.png"));
            fireIron.SetTexture("_BumpMap", ImportNormal("fire_iron_normal.png"));
            SaveMaterial(fireIron);
            AssetImporter.GetAtPath($"{MaterialsDir}/{FireIronMaterialName}.mat").SetAssetBundleNameAndVariant(BundleName, "");

            // The firebox's walls, lit only by the fire; the mod gives each
            // a copy with its shell's texture.
            var wall = new Material(FindShader(FireboxWallShaderName)) { name = FireboxWallMaterialName };
            SaveMaterial(wall);
            AssetImporter.GetAtPath($"{MaterialsDir}/{FireboxWallMaterialName}.mat").SetAssetBundleNameAndVariant(BundleName, "");

            // The firebox's flames and smoke as one volume over the coal; the
            // mod gives each firebox a copy.
            var volume = new Material(FindShader(FireboxVolumeShaderName)) { name = FireboxVolumeMaterialName };
            volume.SetTexture("_Noise", ImportVolume("fire_volume_noise.bytes", FireVolumeNoiseSize));
            SaveMaterial(volume);
            AssetImporter.GetAtPath($"{MaterialsDir}/{FireboxVolumeMaterialName}.mat").SetAssetBundleNameAndVariant(BundleName, "");

            // The coal on the grate as lumps; the mod builds their mesh.
            var coalLumps = new Material(FindShader(CoalLumpsShaderName)) { name = CoalLumpsMaterialName };
            coalLumps.SetTexture("_Noise", volume.GetTexture("_Noise"));
            SaveMaterial(coalLumps);
            AssetImporter.GetAtPath($"{MaterialsDir}/{CoalLumpsMaterialName}.mat").SetAssetBundleNameAndVariant(BundleName, "");

            // The chimney's smoke drawn at half resolution: the scene's depth
            // laid at that size, and the smoke laid over the scene.
            var depth = new Material(FindShader(PlumeDepthShaderName)) { name = PlumeDepthMaterialName };
            SaveMaterial(depth);
            AssetImporter.GetAtPath($"{MaterialsDir}/{PlumeDepthMaterialName}.mat").SetAssetBundleNameAndVariant(BundleName, "");
            var composite = new Material(FindShader(PlumeCompositeShaderName)) { name = PlumeCompositeMaterialName };
            SaveMaterial(composite);
            AssetImporter.GetAtPath($"{MaterialsDir}/{PlumeCompositeMaterialName}.mat").SetAssetBundleNameAndVariant(BundleName, "");
            foreach (var icon in new[] { "fire_iron_icon.png", "fire_iron_icon_dropped.png" })
                ImportIcon(icon).SetAssetBundleNameAndVariant(BundleName, "");

            // The fire overlay's typeface, when its files are here (the public
            // source leaves them out, and the overlay falls back to the game's font).
            foreach (var font in Directory.Exists(FontsDir) ? Directory.GetFiles(FontsDir, "*.ttf") : new string[0])
                AssetImporter.GetAtPath(font.Replace('\\', '/')).SetAssetBundleNameAndVariant(BundleName, "");

            AssetImporter.GetAtPath(PrefabPath).SetAssetBundleNameAndVariant(BundleName, "");
            AssetImporter.GetAtPath(OverflowPrefabPath).SetAssetBundleNameAndVariant(BundleName, "");
            AssetImporter.GetAtPath(FireboxPrefabPath).SetAssetBundleNameAndVariant(BundleName, "");
            Directory.CreateDirectory(output);
            var manifest = BuildPipeline.BuildAssetBundles(output,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new InvalidOperationException("Asset bundle build failed; see the editor log");
            Debug.Log($"Realistic Firing FX: built '{BundleName}' into {Path.GetFullPath(output)}");
            BuildStackBundle(output);
        }

        // Realistic Stack Effects' bundle: the plume's prefab and its
        // half-resolution materials, their shaders and textures copied in.
        // An asset takes one bundle by its importer's name, so this one is
        // listed outright and built on its own, then laid beside the other.
        private static void BuildStackBundle(string output)
        {
            var own = Path.Combine(output, "stackeffects");
            Directory.CreateDirectory(own);
            var build = new AssetBundleBuild
            {
                assetBundleName = StackBundleName,
                assetNames = new[]
                {
                    PrefabPath,
                    $"{MaterialsDir}/{PlumeCompositeMaterialName}.mat",
                    $"{MaterialsDir}/{PlumeDepthMaterialName}.mat",
                },
            };
            var manifest = BuildPipeline.BuildAssetBundles(own, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new InvalidOperationException("Stack effects bundle build failed; see the editor log");
            File.Copy(Path.Combine(own, StackBundleName), Path.Combine(output, StackBundleName), true);
            Debug.Log($"Realistic Firing FX: built '{StackBundleName}' into {Path.GetFullPath(output)}");
        }

        private static Texture2D ImportFlipbook(string file, bool data = false)
        {
            var path = $"{TexturesDir}/{file}";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                           ?? throw new FileNotFoundException($"Missing texture {path}; run fx/textures/make_smoke_textures.py");
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = !data;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material CreateMaterial(Shader shader, string name, Texture2D texture)
        {
            Directory.CreateDirectory(MaterialsDir);
            var path = $"{MaterialsDir}/Smoke{name}.mat";
            AssetDatabase.DeleteAsset(path);
            var material = new Material(shader) { name = $"Smoke{name}", mainTexture = texture };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreatePrefab(Material puff, Material jet, Material spark, Material heat)
        {
            var root = new GameObject(PrefabName);
            try
            {
                // The narrow, fast streaks right above the stack, stretched along their motion.
                var jetSystem = CreateSystem(root, "Jet", jet, tiles: 4, lifetime: (0.3f, 0.55f), maxParticles: 600,
                    growth: Curve(0f, 0.6f, 1f, 1.4f),
                    fade: Fade((0f, 0f), (0.1f, 0.85f), (0.6f, 0.5f), (1f, 0f)));
                var jetRenderer = jetSystem.GetComponent<ParticleSystemRenderer>();
                jetRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                jetRenderer.velocityScale = 0.06f;
                jetRenderer.lengthScale = 1.5f;

                // The billowing cloud: fades in as the jet fades out, swells, and thins out late.
                // Every smoke particle starts at a quarter of its size, so nothing
                // spills wider than the stack's mouth before it has left it.
                // Its puffs roll upright (the shader picks their frames), so
                // the steam rolls out over their tops and down their sides.
                CreateSystem(root, "Cloud", puff, tiles: 1, lifetime: (6f, 10f), maxParticles: 1500,
                    growth: Curve(0f, 0.25f, 0.15f, 1f, 1f, 2.4f),
                    fade: Fade((0f, 0f), (0.06f, 0.95f), (0.45f, 0.7f), (1f, 0f)), noise: 0.35f, upright: true);

                // A smaller, darker core when the fire is sooty.
                CreateSystem(root, "Soot", puff, tiles: 1, lifetime: (4f, 7f), maxParticles: 600,
                    growth: Curve(0f, 0.25f, 0.15f, 1f, 1f, 2f),
                    fade: Fade((0f, 0f), (0.05f, 1f), (0.4f, 0.75f), (1f, 0f)), noise: 0.3f, upright: true);

                // Cinders: drawn as streaks along their flight, cooling from
                // yellow-white through orange to dull red as they burn out.
                // The mod launches them and applies drag and gravity itself.
                var sparks = CreateSystem(root, "Sparks", spark, tiles: 1, lifetime: (1.5f, 3f), maxParticles: 400,
                    growth: Curve(0f, 1f, 1f, 0.6f),
                    fade: Fade((0f, 1f), (0.6f, 1f), (1f, 0f)));
                var cooling = sparks.colorOverLifetime;
                cooling.color = new ParticleSystem.MinMaxGradient(Cooling());
                var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
                sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                sparkRenderer.velocityScale = 0.035f;
                sparkRenderer.lengthScale = 1f;

                // The hot gas's shimmer just over the stack: swelling and
                // fading out within a couple of seconds as it mixes with the air.
                CreateSystem(root, "Heat", heat, tiles: 1, lifetime: (1.2f, 1.8f), maxParticles: 200,
                    growth: Curve(0f, 0.6f, 1f, 2.4f),
                    fade: Fade((0f, 0f), (0.15f, 1f), (0.6f, 0.6f), (1f, 0f)));

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // An injector's overflow: water and steam spilling from the pipe under
        // the cab while it picks up or after it knocks off. The mod points the
        // root (its +Z) out and down and sets both rates from the spill; these
        // systems emit on their own and fall or rise under their own gravity.
        private static Shader FindShader(string name) =>
            Shader.Find(name) ?? throw new InvalidOperationException($"Shader '{name}' did not compile; see the editor log");

        // A data texture (not colors): linear, repeating, alpha as data.
        private static Texture2D ImportData(string file)
        {
            var path = $"{TexturesDir}/{file}";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                           ?? throw new FileNotFoundException($"Missing texture {path}; run fx/textures/make_firebox_textures.py");
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // A tiling 3D data texture from raw RGBA8 texels (x fastest, then y,
        // then z): linear, repeating, filtered, saved as an asset of its own.
        private static Texture3D ImportVolume(string file, int size)
        {
            var path = $"{TexturesDir}/{file}";
            AssetDatabase.ImportAsset(path);
            var raw = AssetDatabase.LoadAssetAtPath<TextAsset>(path)
                      ?? throw new FileNotFoundException($"Missing {path}; run fx/textures/make_fire_volume.py");
            var bytes = raw.bytes;
            if (bytes.Length != size * size * size * 4)
                throw new InvalidOperationException($"{path} has {bytes.Length} bytes, not {size}^3 RGBA texels");
            var pixels = new Color32[size * size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(bytes[4 * i], bytes[4 * i + 1], bytes[4 * i + 2], bytes[4 * i + 3]);
            var texture = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
            {
                name = Path.GetFileNameWithoutExtension(file),
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);
            var assetPath = $"{TexturesDir}/{texture.name}.asset";
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(texture, assetPath);
            return AssetDatabase.LoadAssetAtPath<Texture3D>(assetPath);
        }

        // An inventory icon: full resolution, sharp, with transparency.
        private static TextureImporter ImportIcon(string file)
        {
            var path = $"{TexturesDir}/{file}";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                           ?? throw new FileNotFoundException($"Missing texture {path}; run fx/textures/make_firebox_textures.py");
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return importer;
        }

        // A colour texture with smoothness in its alpha (not transparency).
        private static Texture2D ImportAlbedo(string file)
        {
            var path = $"{TexturesDir}/{file}";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                           ?? throw new FileNotFoundException($"Missing texture {path}; run fx/textures/make_firebox_textures.py");
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // A tiling normal map.
        private static Texture2D ImportNormal(string file)
        {
            var path = $"{TexturesDir}/{file}";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                           ?? throw new FileNotFoundException($"Missing texture {path}; run fx/textures/make_firebox_textures.py");
            importer.textureType = TextureImporterType.NormalMap;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void SaveMaterial(Material material)
        {
            Directory.CreateDirectory(MaterialsDir);
            var path = $"{MaterialsDir}/{material.name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
        }

        // The firebox: the fire bed's renderer (the mod builds its mesh), the
        // flames, and green coal's smoke. The mod emits every particle in the
        // firebox's own space, so they ride with the loco.
        private static void CreateFireboxPrefab(Material coalBed, Material flame, Material smoke, Material embers, Material flameCards)
        {
            var root = new GameObject(FireboxPrefabName);
            try
            {
                // The mod builds these meshes: the dark lumps on top, the
                // glowing embers under them, and the flame cards on the bed.
                MeshHolder(root, "Bed", coalBed);
                MeshHolder(root, "Embers", embers);
                MeshHolder(root, "FlameCards", flameCards);

                var flames = CreateSystem(root, "Flames", flame, tiles: 4, lifetime: (0.35f, 0.7f), maxParticles: 800,
                    growth: Curve(0f, 0.55f, 0.3f, 1f, 1f, 0.75f),
                    fade: Fade((0f, 0f), (0.12f, 1f), (0.65f, 0.75f), (1f, 0f)), noise: 0.12f);
                var smokeSystem = CreateSystem(root, "Smoke", smoke, tiles: 8, lifetime: (1.4f, 2.8f), maxParticles: 700,
                    growth: Curve(0f, 0.45f, 1f, 1.8f),
                    fade: Fade((0f, 0f), (0.12f, 0.8f), (0.6f, 0.6f), (1f, 0f)), noise: 0.3f);
                // Flames are given a width and a height each: taller and narrower as the draft draws them.
                var flameMain = flames.main;
                flameMain.startSize3D = true;
                foreach (var system in new[] { flames, smokeSystem })
                {
                    var main = system.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var renderer = system.GetComponent<ParticleSystemRenderer>();
                    renderer.maxParticleSize = 1f;
                }
                PrefabUtility.SaveAsPrefabAsset(root, FireboxPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void MeshHolder(GameObject root, string name, Material material)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(root.transform, false);
            holder.AddComponent<MeshFilter>();
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void CreateOverflowPrefab(Material puff, Material droplet)
        {
            var root = new GameObject(OverflowPrefabName);
            try
            {
                var water = CreateSystem(root, "Water", droplet, tiles: 1, lifetime: (0.5f, 1f), maxParticles: 600,
                    growth: Curve(0f, 1f, 1f, 1.3f),
                    fade: Fade((0f, 0.9f), (0.7f, 0.8f), (1f, 0f)));
                var waterMain = water.main;
                waterMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
                waterMain.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                waterMain.startColor = new Color(0.85f, 0.92f, 1f, 0.85f);
                waterMain.gravityModifier = 1f;
                OverflowShape(water, angle: 18f);
                var waterRenderer = water.GetComponent<ParticleSystemRenderer>();
                waterRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                waterRenderer.velocityScale = 0.04f;
                waterRenderer.lengthScale = 1f;

                var steam = CreateSystem(root, "Steam", puff, tiles: 8, lifetime: (1f, 2f), maxParticles: 300,
                    growth: Curve(0f, 0.3f, 0.3f, 1f, 1f, 2f),
                    fade: Fade((0f, 0f), (0.1f, 0.8f), (0.5f, 0.5f), (1f, 0f)), noise: 0.4f, variants: true);
                var steamMain = steam.main;
                steamMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
                steamMain.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                steamMain.startColor = new Color(0.95f, 0.95f, 0.95f, 0.8f);
                steamMain.gravityModifier = -0.15f;
                OverflowShape(steam, angle: 30f);

                // Unlike the chimney, these emit on their own at rates the mod sets.
                foreach (var system in new[] { water, steam })
                {
                    var emission = system.emission;
                    emission.enabled = true;
                    emission.rateOverTime = 0f;
                }
                PrefabUtility.SaveAsPrefabAsset(root, OverflowPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void OverflowShape(ParticleSystem system, float angle)
        {
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.02f;
        }

        // With variants, each row of the sheet is its own wisp over its life,
        // and a particle plays a row picked at random. Upright, a particle
        // stands within a few degrees of the view's up and hardly turns.
        private static ParticleSystem CreateSystem(GameObject parent, string name, Material material, int tiles,
            (float min, float max) lifetime, int maxParticles, AnimationCurve growth, Gradient fade, float noise = 0f,
            bool variants = false, bool upright = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startRotation = upright ? new ParticleSystem.MinMaxCurve(-0.2f, 0.2f) : new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            if (upright)
            {
                var spin = system.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            }
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;

            // The mod emits every particle itself.
            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3f;
            shape.radius = 0.15f;

            var sheet = system.textureSheetAnimation;
            sheet.enabled = tiles > 1;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = tiles;
            sheet.numTilesY = tiles;
            sheet.animation = variants ? ParticleSystemAnimationType.SingleRow : ParticleSystemAnimationType.WholeSheet;
            if (variants)
                sheet.rowMode = ParticleSystemAnimationRowMode.Random;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, growth);

            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);

            if (noise > 0f)
            {
                // No turbulence at birth, so the jet leaves the stack straight;
                // it builds over the first third of the particle's life.
                var turbulence = system.noise;
                turbulence.enabled = true;
                turbulence.strength = new ParticleSystem.MinMaxCurve(noise, Curve(0f, 0f, 0.3f, 1f, 1f, 1f));
                turbulence.frequency = 0.4f;
                turbulence.scrollSpeed = 0.3f;
                turbulence.damping = true;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            // A surface the chimney smoke shader lights needs the particle's tangent.
            if (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f)
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
                    ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1XYZW, ParticleSystemVertexStream.Custom2XYZW,
                    ParticleSystemVertexStream.Center, ParticleSystemVertexStream.Tangent,
                });
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 3f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static AnimationCurve Curve(params float[] timeValuePairs)
        {
            var keys = new Keyframe[timeValuePairs.Length / 2];
            for (var i = 0; i < keys.Length; i++)
                keys[i] = new Keyframe(timeValuePairs[2 * i], timeValuePairs[2 * i + 1]);
            var curve = new AnimationCurve(keys);
            for (var i = 0; i < keys.Length; i++)
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            return curve;
        }

        private static Gradient Cooling()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.12f), 0.3f),
                    new GradientColorKey(new Color(0.7f, 0.15f, 0.03f), 0.75f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        // White over life; the mod's per-particle color tints it. Only alpha fades.
        private static Gradient Fade(params (float time, float alpha)[] keys)
        {
            var gradient = new Gradient();
            var alphas = new GradientAlphaKey[keys.Length];
            for (var i = 0; i < keys.Length; i++)
                alphas[i] = new GradientAlphaKey(keys[i].alpha, keys[i].time);
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            return gradient;
        }
    }
}
