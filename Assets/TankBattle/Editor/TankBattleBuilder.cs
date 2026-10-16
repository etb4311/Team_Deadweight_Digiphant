using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DigiPhant;

namespace DeadweightTanks.Editor
{
    // Builds Assets/TankBattle/Scenes/TankBattle.unity from code, so the arena can be regenerated after tweaks.
    // Only writes inside Assets/TankBattle/. Existing scenes, prefabs and scripts are read, never changed.
    public static class TankBattleBuilder
    {
        public const string ScenePath = "Assets/TankBattle/Scenes/TankBattle.unity";
        const string MaterialFolder = "Assets/TankBattle/Generated";
        const string ElephantPrefab = "Assets/Elephant/Prefabs/Elephant.prefab";
        const string Clips = "Assets/Elephant/Animations/";

        public const float Width = 44, Depth = 30;   // arena floor, metres (an elephant is ~4 m across)
        public const float PondRadius = 4.5f;
        static readonly Color[] PlayerColors = { new Color(.91f, .1f, .17f), new Color(.16f, .5f, .95f), new Color(.98f, .8f, .1f) };
        // Spawns form a triangle round the central pond; each faces the pond.
        static readonly Vector3[] Spawns = { new Vector3(-16, 0, -8), new Vector3(16, 0, -8), new Vector3(0, 0, 11.5f) };

        [MenuItem("Tank Battle/Build Arena Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Rebuild Tank Battle arena?",
                    "This regenerates " + ScenePath + " (only the tank scene). Changes made by hand in that scene are lost.", "Rebuild", "Cancel"))
                return;
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EnsureFolder(MaterialFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Savannah course look: its materials and models are reused (read only, never modified).
            savannah = AssetDatabase.LoadAssetAtPath<GameObject>(SavannahPrefab);
            if (!savannah) throw new Exception("Missing " + SavannahPrefab + " (the Savannah course provides the arena's logs and bushes)");
            var sand = SavannahMat("Ochre ground", () => Mat("Sand", new Color(.83f, .7f, .48f)));
            var stone = SavannahMat("Basin stone", () => Mat("Wall stone", new Color(.55f, .5f, .45f)));
            var rock = stone;
            var water = SavannahMat("Still blue water", () => Mat("Pond water", new Color(.2f, .52f, .7f), smoothness: .9f));
            var mud = SavannahMat("Pale sand path", () => Mat("Pond mud", new Color(.45f, .36f, .24f)));

            var arena = new GameObject("Tank Arena").transform;
            Block(arena, "Ground", PrimitiveType.Cube, sand, new Vector3(0, -.1f, 0), new Vector3(Width + 2, .2f, Depth + 2));
            const float wallH = 2, wallT = 1;
            Block(arena, "Wall north", PrimitiveType.Cube, stone, new Vector3(0, wallH / 2, Depth / 2 + wallT / 2), new Vector3(Width + 2 * wallT, wallH, wallT));
            Block(arena, "Wall south", PrimitiveType.Cube, stone, new Vector3(0, wallH / 2, -Depth / 2 - wallT / 2), new Vector3(Width + 2 * wallT, wallH, wallT));
            Block(arena, "Wall east", PrimitiveType.Cube, stone, new Vector3(Width / 2 + wallT / 2, wallH / 2, 0), new Vector3(wallT, wallH, Depth));
            Block(arena, "Wall west", PrimitiveType.Cube, stone, new Vector3(-Width / 2 - wallT / 2, wallH / 2, 0), new Vector3(wallT, wallH, Depth));

            // Pond: walk-in (no solid collider). TankPond marks where drinking works.
            var pond = new GameObject("Pond").transform;
            pond.SetParent(arena, false);
            Block(pond, "Mud rim", PrimitiveType.Cylinder, mud, new Vector3(0, .01f, 0), new Vector3(PondRadius * 2 + 2, .01f, PondRadius * 2 + 2), solid: false);
            Block(pond, "Water", PrimitiveType.Cylinder, water, new Vector3(0, .03f, 0), new Vector3(PondRadius * 2, .01f, PondRadius * 2), solid: false);
            pond.gameObject.AddComponent<TankPond>().drinkRadius = PondRadius + 1; // water plus a wading margin

            // Cover: mirrored left/right, plus pieces guarding each spawn and the pond approaches.
            var cover = new GameObject("Cover").transform;
            cover.SetParent(arena, false);
            foreach (float side in new[] { -1f, 1f })
            {
                LogPile(cover, new Vector3(side * 9.5f, 0, -2.5f), side * 25);
                Rock(cover, rock, new Vector3(side * 12, 0, 6), 2.2f);
                Rock(cover, rock, new Vector3(side * 6, 0, -10), 1.8f);
                Bush(cover, new Vector3(side * 17, 0, 3), 1.5f);
                LogPile(cover, new Vector3(side * 15, 0, 11.5f), 0);
            }
            LogPile(cover, new Vector3(0, 0, 7f), 0);
            Bush(cover, new Vector3(-5.5f, 0, 12), 1.2f);
            Bush(cover, new Vector3(5.5f, 0, 12), 1.2f);
            Rock(cover, rock, new Vector3(0, 0, -11), 2);

            // Scenery: acacias just outside the walls (no colliders).
            var scenery = new GameObject("Acacias (scenery)").transform;
            scenery.SetParent(arena, false);
            Vector3[] trees = { new Vector3(-26, 0, 14), new Vector3(26, 0, 14), new Vector3(-27, 0, -4), new Vector3(27, 0, -4),
                                new Vector3(-12, 0, 19), new Vector3(12, 0, 19), new Vector3(-24, 0, -18), new Vector3(24, 0, -18) };
            for (int t = 0; t < trees.Length; t++)
            {
                var tree = CopySavannah("Acacia " + (t % 4 + 1).ToString("00"), scenery);
                tree.transform.localScale = Vector3.one * 1.4f;
                tree.transform.SetLocalPositionAndRotation(trees[t], Quaternion.Euler(0, t * 47, 0));
            }

            // DigiPhant input: same controller as the demo, for 3 seated players fed by the tank bridge.
            var controls = new GameObject("DigiPhant Controls");
            var controller = controls.AddComponent<DigiPhantController>();
            controller.performerCount = 3;
            controller.upperBodyOnly = true;
            controller.inputMode = InputMode.Camera;
            // No bones: these only make "Set neutral pose" wait until every player's hands are visible.
            controller.controls = Enumerable.Range(1, 3).SelectMany(p => new[]
            {
                new BoneControl { label = $"P{p} left hand", performer = p, movement = Movement.LeftHandHeight, bones = new Transform[0], degrees = 0 },
                new BoneControl { label = $"P{p} right hand", performer = p, movement = Movement.RightHandHeight, bones = new Transform[0], degrees = 0 },
            }).ToArray();
            // The panel calls DigiPhantLocomotion when it's present; a disabled-locomotion one keeps the panel safe
            // (no demo elephant here: the tanks move themselves).
            var locomotion = controls.AddComponent<DigiPhantLocomotion>();
            locomotion.enableLocomotion = false;
            locomotion.followElephant = false;
            var preview = controls.AddComponent<DigiPhantCameraPreview>();
            preview.autoStartBridge = false; // never the regular bridge here; TankBridgeLauncher starts the lane tracker
            controls.AddComponent<TankBridgeLauncher>();
            controls.AddComponent<TankSoloTest>(); // F1 in Play: calibrate with one player for testing alone
            var gestures = controls.AddComponent<TankGestures>();
            var shotMat = Mat("Water shot", new Color(.45f, .85f, 1), smoothness: .9f, emission: true);
            var splashMat = ParticleMat("Water splash", new Color(.6f, .85f, 1, .9f));
            var theme = Type.GetType("TeamDeadweight.DeadweightTheme, Assembly-CSharp");
            if (theme != null) controls.AddComponent(theme);

            // Elephants.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ElephantPrefab);
            if (!prefab) throw new Exception("Missing " + ElephantPrefab);
            var players = new GameObject("Tank Elephants").transform;
            for (int i = 0; i < 3; i++)
            {
                var e = (GameObject)PrefabUtility.InstantiatePrefab(prefab, players);
                e.name = "Elephant P" + (i + 1);
                e.transform.position = Spawns[i];
                e.transform.rotation = Quaternion.LookRotation(-Spawns[i].normalized, Vector3.up);
                // The vendor demo script uses the legacy Input class and would throw errors here.
                foreach (var mb in e.GetComponents<MonoBehaviour>())
                    if (mb && mb.GetType().Name == "Elephant") UnityEngine.Object.DestroyImmediate(mb);
                var tank = e.AddComponent<TankElephant>();
                tank.player = i + 1;
                tank.color = PlayerColors[i];
                tank.controller = controller;
                tank.gestures = gestures;
                tank.shotMaterial = shotMat;
                tank.shotBounces = 2;
                tank.splashMaterial = splashMat;
                tank.attack = Clip("elephant@attack.fbx");
                tank.drink = Clip("elephant@drink.fbx");
                tank.die = Clip("elephant@die.fbx");
                tank.keys = TankKeys.ForPlayer(i + 1);
                tank.idle = Clip("elephant@idle.fbx");
                tank.walk = Clip("In-place/elephant@walk.fbx");
                tank.run = Clip("In-place/elephant@run.fbx");
                tank.backward = Clip("In-place/elephant@backward.fbx");
                tank.turnLeft = Clip("elephant@turnleft.fbx");
                tank.turnRight = Clip("elephant@turnright.fbx");
                tank.motionRoot = e.GetComponentsInChildren<Transform>(true).First(t => t.name == "elephant_Main_ctrl");
                var ring = Block(e.transform, "Player ring", PrimitiveType.Cylinder, Mat("Player " + (i + 1), PlayerColors[i], emission: true),
                    new Vector3(0, .08f, 0), new Vector3(4.6f, .01f, 4.6f), solid: false); // above the water surface
                ring.localRotation = Quaternion.identity;
            }

            // Rounds and HUD.
            var match = controls.AddComponent<TankMatch>();
            match.controller = controller;
            match.elephants = players.GetComponentsInChildren<TankElephant>(true).OrderBy(t => t.player).ToArray();
            match.preview = preview;

            // Camera and light.
            var cam = Camera.main;
            cam.fieldOfView = 40;
            cam.farClipPlane = 300;
            var arenaCam = cam.gameObject.AddComponent<TankArenaCamera>();
            arenaCam.arenaHalfSize = new Vector2(Width / 2 + 1, Depth / 2 + 1);
            arenaCam.Place();
            match.arenaCamera = arenaCam;
            var sun = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (sun) sun.transform.rotation = Quaternion.Euler(55, -35, 0);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Tank Battle arena built: " + ScenePath);
        }

        // ---------- validation ----------

        [MenuItem("Tank Battle/Validate Arena (Phase 2)")]
        public static void Validate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) Build();
            if (!File.Exists(ScenePath)) return; // build cancelled
            try { RunChecks(); }
            finally { EditorSceneManager.OpenScene(ScenePath); } // reload, discarding test moves
            Debug.Log("Tank Battle phase 2 validation passed.");
        }

        static void RunChecks()
        {
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            EditorSceneManager.OpenScene(ScenePath);
            var tanks = UnityEngine.Object.FindObjectsByType<TankElephant>().OrderBy(t => t.player).ToArray();
            try { RunChecks(tanks, Check); }
            finally { foreach (var t in tanks) if (t) t.StopAnimation(); } // edit mode never calls OnDisable
        }

        static void RunChecks(TankElephant[] tanks, Action<bool, string> Check)
        {
            Check(tanks.Length == 3 && tanks.Select(t => t.player).SequenceEqual(new[] { 1, 2, 3 }), "Expected elephants P1-P3");
            Check(tanks.All(t => !t.GetComponents<MonoBehaviour>().Any(m => m.GetType().Name == "Elephant")), "Vendor Elephant script left on a tank");
            float now = 0;
            void Run(TankElephant t, float forward, float turn, float seconds)
            {
                t.useTestInput = true; t.testForward = forward; t.testTurn = turn; t.inputSmoothing = 0;
                for (float s = 0; s < seconds; s += 1 / 60f) { Physics.SyncTransforms(); t.Simulate(now += 1 / 60f, 1 / 60f); }
            }
            foreach (var t in tanks) Run(t, 0, 0, 1 / 60f); // edit mode skips Awake; this registers every elephant
            var p1 = tanks[0];

            Vector3 start = p1.transform.position;
            Run(p1, .5f, 0, 1);
            Check(Vector3.Distance(start, p1.transform.position) > 1 && p1.Action == "Walk", "P1 did not walk forward");

            float yaw = p1.transform.eulerAngles.y;
            start = p1.transform.position;
            Run(p1, 0, 1, .5f);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw, p1.transform.eulerAngles.y)) > 30 && Vector3.Distance(start, p1.transform.position) < .01f
                  && p1.Action == "Turn right", "P1 did not turn on the spot");

            // Face west and drive into the wall for a long time: must stop at it.
            p1.transform.rotation = Quaternion.Euler(0, -90, 0);
            p1.transform.position = new Vector3(-12, p1.transform.position.y, -6);
            Run(p1, 1, 0, 6);
            Check(p1.transform.position.x > -Width / 2 - .01f, "P1 drove through the west wall (x = " + p1.transform.position.x + ")");

            // Drive P2 straight at P3 across the open pond (no cover in the way): elephants must block each other.
            var p2 = tanks[1]; var p3 = tanks[2];
            float y = p3.transform.position.y;
            p3.transform.SetPositionAndRotation(new Vector3(-4, y, 0), Quaternion.identity);
            p2.transform.SetPositionAndRotation(new Vector3(4, y, 0), Quaternion.LookRotation(Vector3.left));
            Run(p2, 1, 0, 4);
            Check(p2.transform.position.x < 2, "P2 did not drive towards P3 (P2 at " + p2.transform.position + ")");
            float gap = Vector3.Distance(p2.transform.position, p3.transform.position);
            Check(gap > 3.8f, $"P2 drove into P3: centres {gap:0.00} m apart (P2 {p2.transform.position}, P3 {p3.transform.position})");

            Run(p1, 0, 0, .2f);
            Check(p1.Action == "Idle" && p1.Speed == 0, "P1 did not stop");
        }

        // ---------- helpers ----------

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // Soft round, see-through droplets (URP particles are square and opaque without a texture).
        static Material ParticleMat(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", Droplet());
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1);                  // transparent
            m.SetFloat("_Blend", 0);                    // alpha
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Droplet()
        {
            string path = MaterialFolder + "/Droplet.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            const int size = 64;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - d) * 2.5f)));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Color color, float smoothness = .15f, bool emission = false)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            m.SetFloat("_Smoothness", smoothness);
            if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * .6f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Transform Block(Transform parent, string name, PrimitiveType shape, Material material, Vector3 position, Vector3 scale, bool solid = true)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            else go.isStatic = true;
            return go.transform;
        }

        // ---------- Savannah course pieces ----------

        const string SavannahPrefab = "Assets/SavannahCourse/Prefabs/SavannahCourse.prefab";
        const string SavannahMaterials = "Assets/SavannahCourse/Generated/v4/";
        static GameObject savannah;

        static Material SavannahMat(string name, Func<Material> fallback)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(SavannahMaterials + name + ".mat");
            return m ? m : fallback();
        }

        // A plain copy of one object from the Savannah prefab (shares its meshes and materials; the prefab is untouched).
        static GameObject CopySavannah(string name, Transform parent)
        {
            var source = savannah.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (!source) throw new Exception("Savannah course has no object named '" + name + "'");
            var copy = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            copy.name = name;
            copy.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            return copy;
        }

        // Box collider round everything the object renders. Call while the object is unrotated.
        static void FitBox(GameObject go, float shrink = 1)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(bounds.center);
            Vector3 s = go.transform.lossyScale;
            box.size = Vector3.Scale(bounds.size, new Vector3(1 / s.x, 1 / s.y, 1 / s.z)) * shrink;
        }

        // Two Savannah fallen logs with a third on top: ~4.8 m long, ~2.3 m high, enough to hide an elephant's body.
        static void LogPile(Transform parent, Vector3 at, float yaw)
        {
            var pile = new GameObject("Log pile");
            pile.transform.SetParent(parent, false);
            const float scale = 1.6f, thickness = .8f * scale;
            Vector3[] spots = { new Vector3(0, 0, -thickness * .5f), new Vector3(0, 0, thickness * .5f), new Vector3(.3f, thickness * .85f, 0) };
            foreach (var spot in spots)
            {
                var log = CopySavannah("03 Fallen log", pile.transform);
                log.transform.localPosition = spot;
                log.transform.localScale = Vector3.one * scale;
            }
            FitBox(pile);
            pile.transform.SetLocalPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
            pile.isStatic = true;
        }

        static void Bush(Transform parent, Vector3 at, float scale)
        {
            var bush = CopySavannah("01 Feeding bush", parent);
            bush.transform.localScale = Vector3.one * scale;
            FitBox(bush, .85f); // foliage is soft at the edges
            bush.transform.SetLocalPositionAndRotation(at, Quaternion.Euler(0, at.x * 13, 0));
            bush.isStatic = true;
        }

        static void Rock(Transform parent, Material m, Vector3 at, float size) =>
            Block(parent, "Rock", PrimitiveType.Sphere, m, at + new Vector3(0, size * .35f, 0), new Vector3(size * 1.4f, size, size * 1.2f));


        static AnimationClip Clip(string file)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(Clips + file).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (!clip) throw new Exception("Missing animation clip in " + Clips + file);
            return clip;
        }
    }
}
