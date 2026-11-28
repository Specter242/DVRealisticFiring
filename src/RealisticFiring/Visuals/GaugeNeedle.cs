using System;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// A drawn gauge's needle (the S282 converted to oil: the atomizer's
    /// steam, the tender's oil), turned to what it reads: its piece modeled
    /// about its pivot, pointing up the dial, facing whoever reads it.
    /// </summary>
    internal sealed class GaugeNeedle : MonoBehaviour
    {
        // The needle follows its reading over this time, s, as a damped gauge does.
        private const float Lag = 0.4f;

        private Func<float> read;
        private Quaternion face;
        private float from, to, low, high, angle;
        private bool set;

        /// <summary>Places <paramref name="hand"/> at <paramref name="gauge"/>'s pivot and turns it to <paramref name="reading"/> each frame.</summary>
        internal static GaugeNeedle Fit(Transform hand, S282OilTenderData.Gauge gauge, Func<float> reading)
        {
            hand.localPosition = new Vector3(gauge.Pivot[0], gauge.Pivot[1], gauge.Pivot[2]);
            var needle = hand.gameObject.AddComponent<GaugeNeedle>();
            needle.face = Quaternion.LookRotation(new Vector3(gauge.Normal[0], gauge.Normal[1], gauge.Normal[2]),
                new Vector3(gauge.Up[0], gauge.Up[1], gauge.Up[2]));
            needle.from = gauge.From;
            needle.to = gauge.To;
            needle.low = gauge.Reads[0];
            needle.high = gauge.Reads[1];
            needle.read = reading;
            needle.LateUpdate();
            return needle;
        }

        /// <summary>Its angle now, degrees clockwise from up as read.</summary>
        internal float Angle => angle;

        private void LateUpdate()
        {
            float value;
            try
            {
                value = read != null ? read() : low;
            }
            catch (Exception)
            {
                value = low;
            }
            var target = Mathf.Lerp(from, to, Mathf.InverseLerp(low, high, value));
            angle = set ? angle + (target - angle) * (1f - Mathf.Exp(-Time.deltaTime / Lag)) : target;
            set = true;
            // Clockwise as read, about the face's normal toward the reader.
            transform.localRotation = face * Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}
