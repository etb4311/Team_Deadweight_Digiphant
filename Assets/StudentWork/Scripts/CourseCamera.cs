using UnityEngine;
using UnityEngine.InputSystem;
using DigiPhant;

namespace TeamDeadweight
{
    // Course views for the main camera, plus a top-down minimap of the whole course.
    //   Chase: high behind the elephant and turning with it, so screen-left always matches a left turn.
    //   North up: bird's-eye that follows the elephant but never rotates.
    //   Whole course: fixed view of the entire loop.
    // Keys: C cycles views, M toggles the minimap. Disabling this restores the original camera.
    [DefaultExecutionOrder(200)] // after locomotion (controller LateUpdate) and PhoneDrift (100) move the elephant
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DigiPhantLocomotion))]
    public class CourseCamera : MonoBehaviour, IStatusLine
    {
        public enum View { Chase, NorthUp, WholeCourse }

        public View view = View.Chase;
        [Tooltip("Empty = the object whose name starts with 'Savannah Course'.")]
        public Transform course;
        [Tooltip("Half the width of the area the whole-course view and minimap show, around the course centre.")]
        public float courseHalfSize = 24;
        public Vector3 courseCentreOffset = new Vector3(0, 0, .5f);

        [Header("Chase")]
        [Range(20, 89)] public float chasePitch = 58;
        public float chaseDistance = 15;
        [Tooltip("How far ahead of the elephant the camera aims.")]
        public float lookAhead = 2;
        [Tooltip("How quickly the camera swings round behind the elephant after a turn.")]
        public float turnFollow = 3;

        [Header("North up")]
        [Range(20, 90)] public float northUpPitch = 75;
        public float northUpDistance = 20;

        [Header("Whole course")]
        [Range(20, 90)] public float wholeCoursePitch = 60;

        [Header("Shared")]
        [Tooltip("How quickly the camera glides to its target. Zero snaps.")]
        public float smoothing = 6;

        [Header("Minimap")]
        public bool showMinimap = true;
        [Range(.1f, .5f), Tooltip("Minimap height as a fraction of the screen.")]
        public float minimapSize = .3f;

        DigiPhantLocomotion locomotion;
        Camera main, minimap;
        RenderTexture minimapTexture; // drawn in OnGUI; a second on-screen camera would squash the IMGUI panels into its viewport
        Rect minimapRect;
        Vector3 savedPosition;
        Quaternion savedRotation;
        bool savedFollow, active;
        float yaw;
        Vector3 lastRoot, cameraPosition; // own smoothed pose, so outside writes (locomotion reset) can't cause a swoop
        Quaternion cameraRotation;
        GUIStyle markerStyle;

        void OnEnable()
        {
            locomotion = GetComponent<DigiPhantLocomotion>();
            if (!course)
                foreach (var t in FindObjectsByType<Transform>())
                    if (t.parent == null && t.name.StartsWith("Savannah Course")) { course = t; break; }
        }

        void OnDisable()
        {
            if (minimap) { minimap.enabled = false; minimap.targetTexture = null; Destroy(minimap.gameObject); }
            if (minimapTexture) { minimapTexture.Release(); Destroy(minimapTexture); }
            if (!active) return;
            active = false;
            if (main) main.transform.SetPositionAndRotation(savedPosition, savedRotation);
            if (locomotion) locomotion.followElephant = savedFollow;
        }

        void LateUpdate()
        {
            var root = locomotion.travelRoot;
            if (!root) return;
            if (!active)
            {
                // Take over the camera locomotion was following; remember it to restore later.
                main = locomotion.followCamera ? locomotion.followCamera : Camera.main;
                if (!main) return;
                // Let locomotion record the stage camera pose before this script moves it.
                if (!locomotion.Initialise()) return;
                savedPosition = main.transform.position;
                savedRotation = main.transform.rotation;
                savedFollow = locomotion.followElephant;
                locomotion.followElephant = false;
                yaw = root.eulerAngles.y;
                active = true;
                Place(root, snap: true);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame) view = (View)(((int)view + 1) % 3);
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame) showMinimap = !showMinimap;

            Place(root, snap: false);
            UpdateMinimap();
        }

        void Place(Transform root, bool snap)
        {
            float dt = Time.deltaTime;
            yaw = Mathf.LerpAngle(yaw, root.eulerAngles.y, snap || turnFollow <= 0 ? 1 : 1 - Mathf.Exp(-turnFollow * dt));
            // Real movement is under 0.5 m per frame, so a bigger jump is a teleport ("Return to starting position").
            bool teleported = (root.position - lastRoot).sqrMagnitude > 1;
            lastRoot = root.position;
            if (teleported) yaw = root.eulerAngles.y;
            Vector3 aim;
            Quaternion rotation;
            float distance;
            switch (view)
            {
                case View.Chase:
                    rotation = Quaternion.Euler(chasePitch, yaw, 0);
                    aim = root.position + Quaternion.Euler(0, yaw, 0) * Vector3.forward * lookAhead;
                    distance = chaseDistance;
                    break;
                case View.NorthUp:
                    rotation = Quaternion.Euler(northUpPitch, 0, 0);
                    aim = root.position;
                    distance = northUpDistance;
                    break;
                default:
                    rotation = Quaternion.Euler(wholeCoursePitch, 0, 0);
                    aim = CourseCentre();
                    // Back off until the course fits the narrower of the two fields of view.
                    float halfFov = Mathf.Min(main.fieldOfView, Camera.VerticalToHorizontalFieldOfView(main.fieldOfView, main.aspect)) * .5f;
                    distance = courseHalfSize * 1.1f / Mathf.Tan(halfFov * Mathf.Deg2Rad);
                    break;
            }
            Vector3 position = aim - rotation * Vector3.forward * distance;
            float blend = snap || teleported || smoothing <= 0 ? 1 : 1 - Mathf.Exp(-smoothing * dt);
            cameraPosition = Vector3.Lerp(cameraPosition, position, blend);
            cameraRotation = Quaternion.Slerp(cameraRotation, rotation, blend);
            main.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        Vector3 CourseCentre() => (course ? course.position : Vector3.zero) + courseCentreOffset;

        void UpdateMinimap()
        {
            if (!showMinimap || view == View.WholeCourse) // the whole-course view already shows the map
            {
                if (minimap) minimap.enabled = false;
                return;
            }
            if (!minimap)
            {
                minimap = new GameObject("Course minimap").AddComponent<Camera>();
                minimap.transform.SetParent(transform, false);
                minimap.CopyFrom(main);
                minimap.orthographic = true;
                minimap.aspect = 1;
                minimap.clearFlags = CameraClearFlags.SolidColor;
                minimap.backgroundColor = new Color(.1f, .1f, .1f);
                minimap.depth = main.depth - 1; // untagged, so Camera.main stays the stage camera
                minimap.rect = new Rect(0, 0, 1, 1);
            }
            int size = Mathf.Max(64, Mathf.RoundToInt(Screen.height * minimapSize));
            if (!minimapTexture || minimapTexture.width != size)
            {
                if (minimapTexture) { minimap.targetTexture = null; minimapTexture.Release(); Destroy(minimapTexture); }
                minimapTexture = new RenderTexture(size, size, 24) { name = "Course minimap" };
                minimap.targetTexture = minimapTexture;
            }
            minimap.enabled = true;
            float margin = Screen.height * .015f;
            minimapRect = new Rect(Screen.width - size - margin, Screen.height - size - margin, size, size);
            minimap.orthographicSize = courseHalfSize;
            minimap.transform.SetPositionAndRotation(CourseCentre() + Vector3.up * 100, Quaternion.Euler(90, 0, 0));
            minimap.nearClipPlane = 1;
            minimap.farClipPlane = 200;
        }

        public string StatusTitle => "Camera";
        public string StatusValue => (view == View.NorthUp ? "North up" : view == View.WholeCourse ? "Whole course" : "Chase")
            + (showMinimap && view != View.WholeCourse ? " + map" : "");
        public bool StatusActive => false;

        void OnGUI()
        {
            if (!DeadweightTheme.ShowsStatus) GUI.Label(new Rect(Screen.width - 270, 70, 260, 22), "Camera: " + view + "  (C view, M map)");
            if (!minimap || !minimap.enabled || !minimapTexture || !locomotion.travelRoot) return;
            // Elephant marker on the minimap: an arrow pointing where it faces.
            var root = locomotion.travelRoot;
            GUI.DrawTexture(minimapRect, minimapTexture);
            DeadweightTheme.Frame(minimapRect, "MAP");
            Vector3 v = minimap.WorldToViewportPoint(root.position);
            if (v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1) return; // off the map edge
            var centre = new Vector2(minimapRect.x + v.x * minimapRect.width, minimapRect.yMax - v.y * minimapRect.height);
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(root.eulerAngles.y, centre);
            if (markerStyle == null)
            {
                markerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
                markerStyle.normal.textColor = new Color(1, .35f, .1f);
            }
            GUI.Label(new Rect(centre.x - 15, centre.y - 15, 30, 30), "▲", markerStyle);
            GUI.matrix = saved;
        }
    }
}
