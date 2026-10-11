using System.Linq;
using DV.Damage;
using DV.Simulation.Ports;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class OilFlareback
    {
        /// <summary>The worst flareback takes this share of the body's health.</summary>
        public const float WorstDamage = 0.08f;

        private const string ClipName = "Steam_Blowback_01";
        private const float FlashIntensity = 8f;
        private const float FlashTime = 0.5f;

        private static AudioClip clip;

        /// <summary>Plays out every flareback the oil fires have had since the last frame.</summary>
        public static void Update()
        {
            foreach (var firebox in RealisticOilFirebox.All)
            {
                var strength = firebox.PendingFlareback;
                if (!(strength > 0f))
                    continue;
                firebox.PendingFlareback = 0f;
                Play(firebox, strength);
            }
        }

        // On the host: the effects here, the damage, and the effects for the
        // players near the loco.
        private static void Play(RealisticOilFirebox firebox, float strength)
        {
            var car = firebox.Car;
            if (car == null)
                return;
            PlayEffects(car, strength);
            var damage = car.GetComponent<DamageController>();
            var taken = 0f;
            if (damage != null && damage.bodyDamage != null)
            {
                taken = damage.bodyDamage.maxHealth * WorstDamage * strength;
                damage.bodyDamage.DamageCar(taken, useSensitivityModifier: false);
            }
            Main.Mod?.Logger.Log($"{car.ID}: flareback at {strength:P0}: {taken:F0} body damage");
            Net.Broadcast(car, FireEvent.Flareback, strength);
        }

        /// <summary>The flareback's sound, flash, and notice, wherever the loco is seen; the damage is the host's.</summary>
        public static void PlayEffects(TrainCar car, float strength)
        {
            var at = DoorPosition(car) ?? car.transform.position + car.transform.up * 2f;
            if (clip == null)
                clip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == ClipName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, at, Mathf.Lerp(0.6f, 1f, strength));

            var flash = new GameObject("oil flareback flash");
            flash.transform.position = at;
            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = Mathf.Lerp(3f, 8f, strength);
            light.intensity = FlashIntensity * Mathf.Lerp(0.4f, 1f, strength);
            flash.AddComponent<Fade>();
            FireController.Announce(Notice);
        }

        private const string Notice = "Flareback! Oil vapor in the firebox went up when it lit. Clear the firebox with the blower " +
                                      "before lighting; then the atomizer, your lighter at the open fire door, and the oil.";

        // The fire door's control in the cab, if the cab is loaded.
        private static Vector3? DoorPosition(TrainCar car)
        {
            var doorPort = car.SimController != null && car.SimController.firebox != null ? car.SimController.firebox.fireboxDoorPortId : null;
            var interior = car.loadedInterior != null ? car.loadedInterior.transform : null;
            var door = doorPort != null && interior != null
                ? interior.GetComponentsInChildren<InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == doorPort)
                : null;
            return door != null ? door.transform.position : (Vector3?)null;
        }

        private sealed class Fade : MonoBehaviour
        {
            private float age;
            private Light flash;

            private void Awake() => flash = GetComponent<Light>();

            private void Update()
            {
                age += Time.deltaTime;
                if (flash != null)
                    flash.intensity *= Mathf.Exp(-Time.deltaTime / (FlashTime / 3f));
                if (age > FlashTime)
                    Destroy(gameObject);
            }
        }
    }
}
