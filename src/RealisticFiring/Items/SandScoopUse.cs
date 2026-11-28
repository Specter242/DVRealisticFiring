using DV.CabControls;
using DV.Interaction;
using DV.Utils;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Sanding the flues with the sand scoop (<see cref="SandScoop"/>): used
    /// empty on an oil burner's sand box it takes a quart of the engine's
    /// sand; used full on the firebox's sand target (<see cref="FlueSandTarget"/>)
    /// with the peephole open it throws the sand in, where the draft carries
    /// it through the flues (<see cref="RealisticOilFirebox.ThrowSand"/>). The
    /// game's working animation takes its spout to each. In VR the spout's
    /// touch does the same.
    /// </summary>
    internal sealed class SandScoopUse : MonoBehaviour, IItemUseAnimated, IInventoryItemLocalizer
    {
        public const string TipName = "[scoop tip]";
        public const string SandName = "sand load";

        // A VR touch acts at most this often, s.
        private const float TouchEvery = 0.8f;

        // Less than this in the box, kg, is none to scoop.
        private const float Least = 0.1f;

        private Transform tip;
        private GameObject sandShown;
        private float touchedAt = float.NegativeInfinity;

        /// <summary>The sand on the scoop, kg.</summary>
        internal float Load { get; private set; }

        public Transform InteractionPoint => this == null ? null : tip != null ? tip : transform;

        private void Awake()
        {
            tip = transform.Find(TipName);
            sandShown = transform.Find(SandName)?.gameObject;
            var spec = GetComponent<InventoryItemSpec>();
            if (spec != null)
                spec.NameOverride = SandScoop.DisplayName;
            Show();
        }

        private static bool Box(ItemUseTarget target, out FlueSandBox box)
        {
            box = target != null ? target.GetComponent<FlueSandBox>() : null;
            return box != null;
        }

        private static bool Hole(ItemUseTarget target, out FlueSandTarget hole)
        {
            hole = target != null ? target.GetComponent<FlueSandTarget>() : null;
            return hole != null;
        }

        public bool IsHoverCompatible(ItemUseTarget target) => Box(target, out _) || Hole(target, out _);

        public bool IsUseCompatible(ItemUseTarget target) =>
            Box(target, out var box) ? CanFill(box)
            : Hole(target, out var hole) && CanThrow(hole, out _);

        private bool CanFill(FlueSandBox box) => !(Load > 0f) && box.Available >= Least;

        private bool CanThrow(FlueSandTarget hole, out RealisticOilFirebox oil) =>
            hole.TryGetFirebox(out oil) && Load > 0f && FlueSandTarget.IsOpen(oil);

        public bool HandleHover(ItemUseTarget target)
        {
            var text = SingletonBehaviour<InteractionTextControllerNonVr>.Instance;
            string line;
            if (Box(target, out var box))
                line = Load > 0f ? $"The scoop holds {Load:F1} kg of sand: throw it in through the open peephole"
                    : box.Available < Least ? box.Why
                    : $"Click to fill the scoop with a quart of sand ({box.Available:F0} kg in the box)";
            else if (Hole(target, out var hole))
                line = !hole.TryGetFirebox(out var oil) ? null
                    : !(Load > 0f) ? "The scoop is empty: fill it at the sand box"
                    : !FlueSandTarget.IsOpen(oil) ? "Open the peephole to throw the sand in"
                    : "Click to throw the sand in: under a hard pull the draft carries it through the flues";
            else
                return false;
            if (text != null && line != null)
                text.DisplayText(line);
            return true;
        }

        public bool HandleUse(ItemUseTarget target)
        {
            if (Box(target, out var box))
                return Fill(box);
            if (Hole(target, out var hole))
                return Throw(hole);
            return false;
        }

        public (Vector3 pos, Quaternion rot) TargetPoint(ItemUseTarget target)
        {
            var pose = target.transform.Find("[animation_pose_target]");
            if (pose != null)
                return (pose.position, Quaternion.Slerp(pose.rotation, InteractionPoint.rotation, 0.4f));
            return (target.transform.position, InteractionPoint.rotation);
        }

        private bool Fill(FlueSandBox box)
        {
            if (!CanFill(box))
                return false;
            Load = box.Scoop(SandScoop.Scoopful);
            Show();
            return Load > 0f;
        }

        private bool Throw(FlueSandTarget hole)
        {
            if (!CanThrow(hole, out var oil))
                return false;
            oil.ThrowSand(Load);
            Load = 0f;
            Show();
            return true;
        }

        private void Show()
        {
            if (sandShown != null)
                sandShown.SetActive(Load > 0f);
        }

        // In VR: the spout dipped in the sand box fills it, put to the open peephole empties it.
        private void OnTriggerEnter(Collider other)
        {
            if (!VRManager.IsVREnabled() || Time.time - touchedAt < TouchEvery || other == null)
                return;
            var item = GetComponent<ItemBase>();
            if (item == null || !item.IsGrabbed())
                return;
            var box = other.GetComponentInParent<FlueSandBox>();
            if (box != null && Fill(box))
            {
                touchedAt = Time.time;
                return;
            }
            var hole = other.GetComponentInParent<FlueSandTarget>();
            if (hole != null && Throw(hole))
                touchedAt = Time.time;
        }

        public string GetNameParam() => SandScoop.DisplayName;

        public string GetCustomDescription() =>
            "An oil burner's sand scoop, kept by the sand box. Fill it with a quart of sand at the sand box, open the peephole, " +
            "and throw it in under a hard pull (blower and firing up, working the engine): the draft carries the sand through " +
            "the flues and scours off the soot, and black smoke at the stack shows it working. Thrown in with little pull, it " +
            "falls in the pan. It comes with the engine: left behind or lost, it is back by the sand box next time.";
    }
}
