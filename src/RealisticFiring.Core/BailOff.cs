using System;

namespace RealisticFiring.Core
{
 public sealed class BailOff
    {
        /// <summary>A released brake cylinder's pressure, absolute bar.</summary>
        public const float Released = 1f;

        private bool requested;

        /// <summary>The automatic application bailed off, bar over released.</summary>
        public float Bailed { get; private set; }

        /// <summary>Bails off at the next step all the automatic application then on.</summary>
        public void Request() => requested = true;

        /// <summary>Whether a bail waits for the next step.</summary>
        public bool Requested => requested;

        /// <summary>
        /// The cylinder pressure the engine's control valve aims for, absolute
        /// bar, from the train brake's <paramref name="automatic"/> target, less
        /// what has been bailed off.
        /// </summary>
        public float Apply(float automatic)
        {
            var applied = Math.Max(0f, automatic - Released);
            if (requested)
            {
                Bailed = applied;
                requested = false;
            }
            // Released, nothing stays bailed; a further reduction applies the engine by its own amount.
            Bailed = Math.Min(Bailed, applied);
            return Released + applied - Bailed;
        }
    }
}
