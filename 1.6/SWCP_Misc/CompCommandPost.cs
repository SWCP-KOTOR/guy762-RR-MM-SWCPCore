using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SWCP_Misc
{
    public class CompProperties_CommandPost : CompProperties
    {
        public float captureRadius;
        public int ticksToCapture;
        public List<FactionDef> nonCapturingFactions;
        public SoundDef soundHeld;
        public SoundDef soundReverting;
        public SoundDef soundFactionless;
        public SoundDef soundCapturing;
        public SoundDef soundBecameFactionless;
        public SoundDef soundCaptured;

        public CompProperties_CommandPost()
        {
            compClass = typeof(CompCommandPost);
        }
    }

    public enum CommandPostState
    {
        Held,
        Reverting,
        Factionless,
        Capturing
    }

    [StaticConstructorOnStartup]
    public class CompCommandPost : ThingComp
    {
        private CommandPostState currentState;
        private float captureProgress;
        private Faction capturingFaction;
        private Sustainer currentSustainer;
        private int tickCounter;
        private bool isContested;
        private bool capturerPresent;
        private bool ownerHostilesPresent;
        private Faction dominantFaction;
        private Material coneMaterial;
        private static Texture2D coneTexture;
        public CompProperties_CommandPost Props => (CompProperties_CommandPost)props;
        public CommandPostState CurrentState => currentState;
        static CompCommandPost()
        {
            var texSize = 128;
            coneTexture = new Texture2D(texSize, texSize, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp
            };

            var colors = new Color32[texSize * texSize];

            for (int y = 0; y < texSize; y++)
            {
                var v = y / (float)(texSize - 1);
                for (int x = 0; x < texSize; x++)
                {
                    var u = (x / (float)(texSize - 1)) * 2f - 1f;
                    var currentWidth = Mathf.Lerp(0.1f, 1f, v);
                    if (Mathf.Abs(u) <= currentWidth)
                    {
                        var distFromCenter = Mathf.Abs(u) / currentWidth;
                        var alphaX = Mathf.SmoothStep(1f, 0f, distFromCenter);
                        var alphaY = Mathf.SmoothStep(1f, 0f, v);
                        var alpha = alphaX * alphaY;
                        alpha = Mathf.Pow(alpha, 0.7f);
                        colors[y * texSize + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255));
                    }
                    else
                    {
                        colors[y * texSize + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
            coneTexture.SetPixels32(colors);
            coneTexture.Apply();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            ScanPawns();
            if (!respawningAfterLoad && parent.Faction != null)
            {
                captureProgress = 1f;
                SetState(CommandPostState.Held);
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent.Map == null || parent.Destroyed) return;

            if (currentSustainer == null || currentSustainer.Ended)
            {
                currentSustainer = null;
                var soundToPlay = GetSoundForState(currentState);
                if (soundToPlay != null)
                {
                    currentSustainer = soundToPlay.TrySpawnSustainer(SoundInfo.InMap(parent, MaintenanceType.PerTick));
                }
            }
            coneMaterial ??=MaterialPool.MatFrom(new MaterialRequest(coneTexture, ShaderDatabase.MoteGlow));
            currentSustainer?.Maintain();

            tickCounter++;
            if (tickCounter >= 60)
            {
                tickCounter = 0;
                ScanPawns();
                UpdateGlower();
            }

            AdvanceState();
        }

        private void ScanPawns()
        {
            var pawnsInRadius = GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.captureRadius, true)
                .OfType<Pawn>()
                .Where(p => !p.Dead && !p.Downed && p.RaceProps.Humanlike && p.Faction != null)
                .Where(p => Props.nonCapturingFactions == null || !Props.nonCapturingFactions.Contains(p.Faction.def))
                .ToList();

            var presentFactions = pawnsInRadius.Select(p => p.Faction).Distinct().ToList();
            ownerHostilesPresent = parent.Faction != null && presentFactions.Any(f => f.HostileTo(parent.Faction));
            capturerPresent = capturingFaction != null && presentFactions.Contains(capturingFaction);
            var hasHostilesToCapturer = capturingFaction != null && presentFactions.Any(f => f.HostileTo(capturingFaction));

            isContested = false;
            for (int i = 0; i < presentFactions.Count; i++)
            {
                for (int j = i + 1; j < presentFactions.Count; j++)
                {
                    if (presentFactions[i].HostileTo(presentFactions[j]))
                    {
                        isContested = true;
                        break;
                    }
                }
            }

            dominantFaction = null;
            if (!isContested && presentFactions.Count > 0)
            {
                var factionGroups = pawnsInRadius.GroupBy(p => p.Faction).ToDictionary(g => g.Key, g => g.Count());
                var maxCount = factionGroups.Values.Max();
                dominantFaction = factionGroups.Where(kvp => kvp.Value == maxCount).RandomElement().Key;
            }

            if (currentState == CommandPostState.Capturing && !capturerPresent)
            {
                isContested = true;
            }
        }

        private void AdvanceState()
        {
            var progressChange = 1f / Props.ticksToCapture;

            switch (currentState)
            {
                case CommandPostState.Held:
                    if (ownerHostilesPresent)
                    {
                        SetState(CommandPostState.Reverting);
                    }
                    break;

                case CommandPostState.Reverting:
                    if (!ownerHostilesPresent)
                    {
                        captureProgress += progressChange;
                        if (captureProgress >= 1f)
                        {
                            captureProgress = 1f;
                            SetState(CommandPostState.Held);
                        }
                    }
                    else
                    {
                        captureProgress -= progressChange;
                        if (captureProgress <= 0f)
                        {
                            captureProgress = 0f;
                            parent.SetFaction(null);
                            SetState(CommandPostState.Factionless);
                            Props.soundBecameFactionless?.PlayOneShot(SoundInfo.InMap(parent));
                        }
                    }
                    break;

                case CommandPostState.Factionless:
                    if (dominantFaction != null && !isContested)
                    {
                        capturingFaction = dominantFaction;
                        capturerPresent = true;
                        SetState(CommandPostState.Capturing);
                    }
                    break;

                case CommandPostState.Capturing:
                    if (isContested || !capturerPresent)
                    {
                        captureProgress -= progressChange;
                        if (captureProgress <= 0f)
                        {
                            captureProgress = 0f;
                            capturingFaction = null;
                            SetState(CommandPostState.Factionless);
                        }
                    }
                    else
                    {
                        captureProgress += progressChange;
                        if (captureProgress >= 1f)
                        {
                            captureProgress = 1f;
                            parent.SetFaction(capturingFaction);
                            SetState(CommandPostState.Held);
                            Props.soundCaptured?.PlayOneShot(SoundInfo.InMap(parent));
                        }
                    }
                    break;
            }
        }

        private SoundDef GetSoundForState(CommandPostState state)
        {
            return state switch
            {
                CommandPostState.Held => Props.soundHeld,
                CommandPostState.Reverting => Props.soundReverting,
                CommandPostState.Factionless => Props.soundFactionless,
                CommandPostState.Capturing => Props.soundCapturing,
                _ => null
            };
        }

        public void SetState(CommandPostState newState)
        {
            if (currentState == newState) return;

            currentState = newState;
            currentSustainer?.End();
            currentSustainer = null;

            var soundToPlay = GetSoundForState(currentState);
            if (soundToPlay != null)
            {
                currentSustainer = soundToPlay.TrySpawnSustainer(SoundInfo.InMap(parent, MaintenanceType.PerTick));
            }
        }

        private void UpdateGlower()
        {
            var glower = parent.GetComp<CompGlower>();
            var targetColor = Color.white;
            if (currentState == CommandPostState.Held || currentState == CommandPostState.Reverting)
            {
                targetColor = parent.Faction?.Color ?? Color.white;
            }
            else if (currentState == CommandPostState.Capturing && capturingFaction != null)
            {
                targetColor = Color.Lerp(Color.white, capturingFaction.Color, captureProgress);
            }

            var targetColorInt = new ColorInt(targetColor);
            if (glower.GlowColor != targetColorInt)
            {
                parent.Map.glowGrid.DeRegisterGlower(glower);
                glower.GlowColor = targetColorInt;
                parent.Map.glowGrid.RegisterGlower(glower);
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            var baseDrawPos = parent.DrawPos;
            baseDrawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();

            var baseScale = 3.45f;
            var currentScale = baseScale * (0.2f + (0.8f * captureProgress));

            var coneDrawPos = baseDrawPos;
            coneDrawPos.z += currentScale / 2f;

            Matrix4x4 matrix = default;
            matrix.SetTRS(coneDrawPos, Quaternion.identity, new Vector3(currentScale, 1f, currentScale));

            Color drawColor;
            if (currentState == CommandPostState.Held || currentState == CommandPostState.Reverting)
            {
                drawColor = Color.Lerp(Color.white, parent.Faction?.Color ?? Color.white, captureProgress);
                drawColor.a = 0.5f + (0.5f * captureProgress);
            }
            else if (currentState == CommandPostState.Capturing && capturingFaction != null)
            {
                drawColor = Color.Lerp(Color.white, capturingFaction.Color, captureProgress);
                drawColor.a = 0.5f + (0.5f * captureProgress);
            }
            else
            {
                drawColor = new Color(1f, 1f, 1f, 0.5f);
            }

            var propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor(ShaderPropertyIDs.Color, drawColor);
            Graphics.DrawMesh(MeshPool.plane10, matrix, coneMaterial, 0, null, 0, propertyBlock);

            if ((currentState == CommandPostState.Held) || (currentState == CommandPostState.Capturing && captureProgress > 0.8f))
            {
                var factionToDraw = currentState == CommandPostState.Held ? parent.Faction : capturingFaction;

                var iconPos = baseDrawPos;
                iconPos.y += 0.05f;
                iconPos.z += 1.5f + (captureProgress * 0.5f);

                Matrix4x4 iconMatrix = default;
                iconMatrix.SetTRS(iconPos, Quaternion.identity, new Vector3(1.5f, 1f, 1.5f));

                var iconMat = MaterialPool.MatFrom(new MaterialRequest(factionToDraw.def.FactionIcon, ShaderDatabase.Cutout));

                var iconAlpha = Mathf.InverseLerp(0.8f, 1.0f, captureProgress);
                var iconColor = factionToDraw.Color;
                iconColor.a = iconAlpha;
                propertyBlock.SetColor(ShaderPropertyIDs.Color, iconColor);

                Graphics.DrawMesh(MeshPool.plane10, iconMatrix, iconMat, 0, null, 0, propertyBlock);
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            currentSustainer?.End();
            currentSustainer = null;
        }

        public override string CompInspectStringExtra()
        {
            var stateStr = "SWCP_CommandPost_State".Translate() + ": ";
            switch (currentState)
            {
                case CommandPostState.Held:
                    stateStr += "SWCP_CommandPost_State_Held".Translate(parent.Faction?.Name ?? "unaligned faction");
                    break;
                case CommandPostState.Factionless:
                    stateStr += "SWCP_CommandPost_State_Factionless".Translate();
                    break;
                case CommandPostState.Capturing:
                    if (capturingFaction != null)
                    {
                        if (isContested || !capturerPresent)
                        {
                            var remainingTicks = Mathf.RoundToInt(captureProgress * Props.ticksToCapture);
                            stateStr += "SWCP_CommandPost_State_LosingProgress".Translate(capturingFaction.Name, remainingTicks.ToStringTicksToPeriod());
                        }
                        else
                        {
                            var remainingTicks = Mathf.RoundToInt((1f - captureProgress) * Props.ticksToCapture);
                            stateStr += "SWCP_CommandPost_State_Capturing".Translate(capturingFaction.Name, remainingTicks.ToStringTicksToPeriod());
                        }
                    }
                    break;
                case CommandPostState.Reverting:
                    if (parent.Faction != null)
                    {
                        var remainingTicks = ownerHostilesPresent ? Mathf.RoundToInt(captureProgress * Props.ticksToCapture) : Mathf.RoundToInt((1f - captureProgress) * Props.ticksToCapture);
                        var key = ownerHostilesPresent ? "SWCP_CommandPost_State_LosingControl" : "SWCP_CommandPost_State_RegainingControl";
                        stateStr += key.Translate(parent.Faction.Name, remainingTicks.ToStringTicksToPeriod());
                    }
                    break;
            }
            return stateStr;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentState, "currentState", CommandPostState.Factionless);
            Scribe_Values.Look(ref captureProgress, "captureProgress", 0f);
            Scribe_References.Look(ref capturingFaction, "capturingFaction");
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
        }
    }
}
