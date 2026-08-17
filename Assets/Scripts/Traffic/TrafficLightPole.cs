using UnityEngine;

namespace TheBlock.Traffic
{
    /// <summary>The four states of an Israeli traffic light, in cycle order.</summary>
    public enum LampState
    {
        Red,
        RedYellow,
        Green,
        Yellow,
    }

    /// <summary>
    /// One pole at one approach. All it does is paint three lamps.
    ///
    /// <b>The model's own lamps are unusable and are deleted at build.</b> The GLB animates coloured
    /// discs sliding behind a semi-transparent lens - the whole model is one BLEND material - which
    /// three.js could not sort and which here would put 230 transparent poles in the transparent
    /// queue for nothing. <c>WorldBuilder.Traffic</c> destroys those three nodes and plants a single
    /// three-submesh quad on the housing face instead, measured from the original lamps' own bounds
    /// rather than from guessed coordinates.
    ///
    /// <b>One renderer, three submeshes, not three renderers.</b> Switching a light is then an
    /// assignment into a three-element shared-material array, and the poles showing the same state
    /// still batch together because they are literally the same material asset. A
    /// <c>MaterialPropertyBlock</c> would have been the obvious reach and is the wrong one - it
    /// gives every pole its own draw call, which is the same mistake U13 avoided on the lot cars'
    /// paint.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficLightPole : MonoBehaviour
    {
        [Tooltip("The three-submesh lamp quad: submesh 0 red, 1 amber, 2 green (top to bottom).")]
        [SerializeField] private MeshRenderer lamps;

        [Tooltip("Six shared materials: red on/off, amber on/off, green on/off.")]
        [SerializeField] private Material redOn;
        [SerializeField] private Material redOff;
        [SerializeField] private Material amberOn;
        [SerializeField] private Material amberOff;
        [SerializeField] private Material greenOn;
        [SerializeField] private Material greenOff;

        private Material[] _slots;
        private LampState _shown;
        private bool _everShown;
        private bool _down;

        /// <summary>
        /// U35h: has this pole been knocked over? While down all three lamps show their OFF material
        /// and <see cref="Apply"/> is a no-op, so <c>TrafficLightSystem</c> can keep painting the
        /// approach without knowing - a felled light that cycles green is worse than no feature.
        /// Setting it back to false forces the next <see cref="Apply"/> to repaint, because the
        /// change-detection latch would otherwise believe the last painted state is still showing.
        ///
        /// The dark materials are the SHARED off assets, never an instance: 233 poles batch by
        /// being literally the same material, and one instanced pole is one more draw call for
        /// every frame it lies there.
        /// </summary>
        public bool Down
        {
            get => _down;
            set
            {
                if (_down == value) return;
                _down = value;
                if (_down)
                {
                    if (lamps == null) return;
                    _slots ??= new Material[3];
                    _slots[0] = redOff;
                    _slots[1] = amberOff;
                    _slots[2] = greenOff;
                    lamps.sharedMaterials = _slots;
                }
                else
                {
                    _everShown = false;
                }
            }
        }

        /// <summary>Set by <c>WorldBuilder.Traffic</c> at build time.</summary>
        public void Configure(
            MeshRenderer lampRenderer,
            Material red, Material redDark,
            Material amber, Material amberDark,
            Material green, Material greenDark)
        {
            lamps = lampRenderer;
            redOn = red;
            redOff = redDark;
            amberOn = amber;
            amberOff = amberDark;
            greenOn = green;
            greenOff = greenDark;
        }

        /// <summary>Paints a state. Cheap to call every frame - it returns immediately unless it changed.</summary>
        public void Apply(LampState state)
        {
            if (lamps == null || _down) return;
            if (_everShown && _shown == state) return;

            _shown = state;
            _everShown = true;

            _slots ??= new Material[3];
            _slots[0] = state is LampState.Red or LampState.RedYellow ? redOn : redOff;
            _slots[1] = state is LampState.Yellow or LampState.RedYellow ? amberOn : amberOff;
            _slots[2] = state == LampState.Green ? greenOn : greenOff;
            lamps.sharedMaterials = _slots;
        }
    }
}
