using System.Collections.Generic;
using TheBlock.Core;
using TheBlock.UI;
using UnityEngine;

namespace TheBlock.Missions
{
    /// <summary>
    /// Drives the campaign cursor against the world — the port of
    /// <c>src/mission/campaign-director.ts</c>.
    ///
    /// It paints the "go here next" objective as a single moving map marker, advances the cursor
    /// when the current mission completes, and reports the objective text and win state the HUD
    /// reads back. Its only side-effect surface is <see cref="MapRegistry"/>; the win CARD belongs
    /// to <see cref="CampaignRunner"/>, which owns every card.
    ///
    /// <b>Can Unity do this better?</b> Yes, and U14 already built the piece: a web POI is a
    /// coordinate snapshot, so its director must remove and re-add the pin every time anything
    /// moves. <see cref="MapPoi.Follow"/> holds a Transform the map reads at DRAW time, so an
    /// objective can ride a walking NPC or a fleeing thief for nothing. The pin is created once per
    /// cursor move rather than per frame either way — but where the web CANNOT track a mover, this
    /// simply does.
    /// </summary>
    public class CampaignDirector : MonoBehaviour
    {
        /// <summary>
        /// One reused dynamic POI, removed and re-added as the cursor moves, so only the current
        /// objective is ever pinned. The name is unique so <see cref="MapRegistry.RemovePoi"/> hits
        /// exactly it.
        /// </summary>
        private const string ObjectivePoi = "Objective";

        [SerializeField] private Campaign campaign;

        private readonly Dictionary<string, Step> _steps = new();
        private bool _cleared; // the win marker-clear happens once

        /// <summary>
        /// A campaign step with its waypoint resolved. The COPY comes from
        /// <c>campaign.config.ts</c>; the coordinates come from the live world config, so a location
        /// stays single-sourced — the pizzeria's pin and the pizzeria are the same number.
        /// </summary>
        private class Step
        {
            public string Objective;

            /// <summary>
            /// The step's map glyph — 🍕 🕺 🚁 🛟, straight out of <c>campaign.config.ts</c>. The web
            /// draws it in place of the objective dot, and until now the port dropped it on the
            /// floor: every landmark and the one place you actually had to go were the same green
            /// circle, which is unreadable exactly when it matters.
            /// </summary>
            public string Emoji;

            public Vector3 Position;
            public Transform Follow;
        }

        private void Awake()
        {
            if (campaign == null) campaign = FindAnyObjectByType<Campaign>();
        }

        /// <summary>
        /// Resolves every step's waypoint from the config, the way <c>game/campaign-setup.ts</c>
        /// does. Called by <see cref="CampaignRunner"/> at Start, once the world exists.
        /// </summary>
        public void BuildSteps(TheBlockConfig.Snapshot snapshot)
        {
            _steps.Clear();
            if (snapshot?.Campaign?.CampaignText == null) return;

            var coords = new Dictionary<string, Vector3>();
            var cfg = snapshot.Config;
            if (cfg?.PizzaPlace != null) coords["pizza"] = Convert.Pos(cfg.PizzaPlace.Position.Raw);
            if (snapshot.Rhythm?.World?.Npc != null)
                coords["dance"] = Convert.Pos(
                    new Vector3(snapshot.Rhythm.World.Npc.X, 0f, snapshot.Rhythm.World.Npc.Z));
            if (cfg?.Vehicle?.Helicopter != null)
                coords["heli"] = Convert.Pos(new Vector3(
                    cfg.Vehicle.Helicopter.Spawn.X, 0f, cfg.Vehicle.Helicopter.Spawn.Z));
            if (cfg?.Vehicle?.Jetski != null)
                coords["jetski"] = Convert.Pos(new Vector3(
                    cfg.Vehicle.Jetski.Spawn.X, 0f, cfg.Vehicle.Jetski.Spawn.Z));

            foreach (var text in snapshot.Campaign.CampaignText)
            {
                if (text?.Id == null) continue;
                if (!coords.TryGetValue(text.Id, out var at)) continue;
                _steps[text.Id] = new Step
                {
                    Objective = text.Objective,
                    Emoji = text.Emoji,
                    Position = at,
                };
            }
        }

        /// <summary>
        /// Where a step begins, in Unity space. U26's Mission Select drops the player here.
        ///
        /// <b>Deliberately the same number the objective pin uses</b>, not a second table. The web
        /// build has two — <c>stepCoords</c> for the marker and a hand-written block inside
        /// <c>campaign-launch.ts</c> for the jump — and they agree only because nobody has moved
        /// anything yet. One dictionary, read twice, cannot drift.
        /// </summary>
        public bool TryStepPosition(string id, out Vector3 position)
        {
            position = Vector3.zero;
            if (id == null || !_steps.TryGetValue(id, out var step)) return false;
            position = step.Follow != null ? step.Follow.position : step.Position;
            return true;
        }

        /// <summary>
        /// Hands a step a live Transform to track instead of its fixed waypoint. Nothing needs it in
        /// Tier 5 — every giver stands still — but it is the reason the pin holds a Transform at
        /// all, and it costs one method.
        /// </summary>
        public void TrackStep(string id, Transform follow)
        {
            if (_steps.TryGetValue(id, out var step)) step.Follow = follow;
        }

        /// <summary>Repaints the objective marker for the current step, or clears it once won.</summary>
        public void Refresh()
        {
            MapRegistry.RemovePoi(ObjectivePoi);
            if (campaign == null || campaign.IsComplete) return; // won: no objective marker

            var current = campaign.Current;
            if (current == null || !_steps.TryGetValue(current.Id, out var step)) return;

            MapRegistry.AddPoi(new MapPoi
            {
                Name = ObjectivePoi,
                Position = step.Position,
                Follow = step.Follow,
                Kind = MapPoiKind.Marker,
                Icon = step.Emoji,
            });
        }

        /// <summary>Advances the cursor when the current mission completes, repainting on a change.</summary>
        public void Tick()
        {
            if (campaign == null) return;
            if (campaign.AdvanceIfComplete()) Refresh();

            // The last step completing does not advance the cursor, so the final marker is cleared
            // here instead — otherwise the finale's pin outlives the campaign.
            if (!_cleared && campaign.IsComplete)
            {
                _cleared = true;
                Refresh();
            }
        }

        /// <summary>Is <paramref name="id"/> the step the player is on right now? The trigger gate.</summary>
        public bool IsCurrent(string id) => campaign != null && campaign.IsCurrent(id);

        /// <summary>
        /// True once the cursor has REACHED or passed <paramref name="id"/> — which is what gates
        /// that step's vehicle. The helicopter is unenterable until the dance is won, and this is
        /// the test that says so.
        /// </summary>
        public bool IsReached(string id)
        {
            if (campaign == null) return false;
            var i = campaign.IndexOfId(id);
            return i >= 0 && campaign.Index >= i;
        }

        /// <summary>The "go here next" line for the HUD, or null once the campaign is won.</summary>
        public string ObjectiveText
        {
            get
            {
                if (campaign == null || campaign.IsComplete) return null;
                var current = campaign.Current;
                if (current == null) return null;
                return _steps.TryGetValue(current.Id, out var step) ? step.Objective : null;
            }
        }

        /// <summary>Every mission done.</summary>
        public bool IsWon => campaign != null && campaign.IsComplete;
    }
}
