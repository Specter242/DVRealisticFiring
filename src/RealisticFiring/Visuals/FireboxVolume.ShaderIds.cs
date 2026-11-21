using UnityEngine;

namespace RealisticFiring.Visuals
{
    internal sealed partial class FireboxVolume
    {
        private static readonly int ZonesAId = Shader.PropertyToID("_ZonesA");
        private static readonly int ZonesBId = Shader.PropertyToID("_ZonesB");
        private static readonly int BoxSizeId = Shader.PropertyToID("_BoxSize");
        private static readonly int BedSId = Shader.PropertyToID("_BedS");
        private static readonly int BedTId = Shader.PropertyToID("_BedT");
        private static readonly int HoleAcrossId = Shader.PropertyToID("_HoleAcross");
        private static readonly int HoleUpId = Shader.PropertyToID("_HoleUp");
        private static readonly int HolePlaneId = Shader.PropertyToID("_HolePlane");
        private static readonly int LeanId = Shader.PropertyToID("_Lean");
        private static readonly int SheetOffsetId = Shader.PropertyToID("_SheetOffset");
        private static readonly int CurlOffsetId = Shader.PropertyToID("_CurlOffset");
        private static readonly int PulseHistoryId = Shader.PropertyToID("_PulseHistory");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");
        private static readonly int StepLengthId = Shader.PropertyToID("_StepLength");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int FlameDensityId = Shader.PropertyToID("_FlameDensity");
        private static readonly int SmokeDensityId = Shader.PropertyToID("_SmokeDensity");
        private static readonly int SmokeAlbedoId = Shader.PropertyToID("_SmokeAlbedo");
        private static readonly int SmokeLightId = Shader.PropertyToID("_SmokeLight");
        private static readonly int SharpId = Shader.PropertyToID("_Sharp");
        private static readonly int EdgeMeanId = Shader.PropertyToID("_EdgeMean");
        private static readonly int SmokeLocalId = Shader.PropertyToID("_SmokeLocal");
        private static readonly int SmokeMeanId = Shader.PropertyToID("_SmokeMean");
        private static readonly int CeilingId = Shader.PropertyToID("_Ceiling");
        private static readonly int DetailBlurId = Shader.PropertyToID("_DetailBlur");
        private static readonly int DitherAmountId = Shader.PropertyToID("_DitherAmount");
        private static readonly int DitherShiftId = Shader.PropertyToID("_DitherShift");
        private static readonly int SmokePlumeId = Shader.PropertyToID("_SmokePlume");
        private static readonly int SmokeStrideId = Shader.PropertyToID("_SmokeStride");
        private static readonly int WarpId = Shader.PropertyToID("_Warp");
        private static readonly int NoiseTileId = Shader.PropertyToID("_NoiseTile");
        private static readonly int SheetLowId = Shader.PropertyToID("_SheetLow");
        private static readonly int SheetHighId = Shader.PropertyToID("_SheetHigh");
        private static readonly int CarpetId = Shader.PropertyToID("_Carpet");
        private static readonly int StretchId = Shader.PropertyToID("_Stretch");
        private static readonly int DebugId = Shader.PropertyToID("_VolumeDebug");
        private static readonly int MixSheetsId = Shader.PropertyToID("_MixSheets");
        private static readonly int MixCurlsId = Shader.PropertyToID("_MixCurls");
        private static readonly int MixLobesId = Shader.PropertyToID("_MixLobes");
        private static readonly int HeatScaleId = Shader.PropertyToID("_HeatScale");
        private static readonly int FlameHeatId = Shader.PropertyToID("_FlameHeat");
        private static readonly int EdgeHeatId = Shader.PropertyToID("_EdgeHeat");
        private static readonly int CoreDepthId = Shader.PropertyToID("_CoreDepth");
        private static readonly int SmokeClumpsId = Shader.PropertyToID("_SmokeClumps");
        private static readonly int RootsId = Shader.PropertyToID("_Roots");
        private static readonly int BeatGlowId = Shader.PropertyToID("_BeatGlow");
        private static readonly int BeatHeightId = Shader.PropertyToID("_BeatHeight");
        private static readonly int BeatHeatId = Shader.PropertyToID("_BeatHeat");
        private static readonly int BeatCoverId = Shader.PropertyToID("_BeatCover");
        private static readonly int LickHeightId = Shader.PropertyToID("_LickHeight");
        private static readonly int LickDepthId = Shader.PropertyToID("_LickDepth");
        private static readonly int LickScaleId = Shader.PropertyToID("_LickScale");
        private static readonly int LickLowId = Shader.PropertyToID("_LickLow");
        private static readonly int LickStrengthId = Shader.PropertyToID("_LickStrength");
        private static readonly int LickGlowId = Shader.PropertyToID("_LickGlow");
        private static readonly int LickHeatId = Shader.PropertyToID("_LickHeat");
        private static readonly int LickMotionId = Shader.PropertyToID("_LickMotion");
        private static readonly int LickStandingId = Shader.PropertyToID("_LickStanding");
        private static readonly int LickEdgeSharpnessId = Shader.PropertyToID("_LickEdgeSharpness");
        private static readonly int LickDetailId = Shader.PropertyToID("_LickDetail");
        private static readonly int LickCurlId = Shader.PropertyToID("_LickCurl");
        private static readonly int LickRoughnessId = Shader.PropertyToID("_LickRoughness");
        private static readonly int LickOffsetId = Shader.PropertyToID("_LickOffset");
        private static readonly int DoorSlopeId = Shader.PropertyToID("_DoorSlope");
        private static readonly int GrateLevelId = Shader.PropertyToID("_GrateLevel");
        private static readonly int BedReliefId = Shader.PropertyToID("_BedRelief");
        private static readonly int BedRiseId = Shader.PropertyToID("_BedRise");
        private static readonly int BoxLightId = Shader.PropertyToID("_BoxLight");
        private static readonly int SwayId = Shader.PropertyToID("_Sway");
        private static readonly int PuffId = Shader.PropertyToID("_Puff");
        private static readonly int BuoyancyId = Shader.PropertyToID("_Buoyancy");
        private static readonly int BaseGlowId = Shader.PropertyToID("_BaseGlow");
        private static readonly int BaseCokeId = Shader.PropertyToID("_BaseCoke");
        private static readonly int FlowCarryId = Shader.PropertyToID("_FlowCarry");
        private static readonly int DetailFlowCarryId = Shader.PropertyToID("_DetailFlowCarry");
        private static readonly int TipWhipId = Shader.PropertyToID("_TipWhip");
        private static readonly int TipClockId = Shader.PropertyToID("_TipClock");
        private static readonly int BaseHeightId = Shader.PropertyToID("_BaseHeight");
        private static readonly int WhiteHeartId = Shader.PropertyToID("_WhiteHeart");
        private static readonly int SpotLowId = Shader.PropertyToID("_SpotLow");
        private static readonly int SpotDimId = Shader.PropertyToID("_SpotDim");
        private static readonly int SpotGlareId = Shader.PropertyToID("_SpotGlare");
        private static readonly int TipHeatId = Shader.PropertyToID("_TipHeat");
        private static readonly int SwayClockId = Shader.PropertyToID("_SwayClock");
        private static readonly int PuffClockId = Shader.PropertyToID("_PuffClock");
        private static readonly int SideWidthId = Shader.PropertyToID("_SideWidth");
        private static readonly int SideSwirlId = Shader.PropertyToID("_SideSwirl");
        private static readonly int FlowAId = Shader.PropertyToID("_FlowA");
        private static readonly int FlowBId = Shader.PropertyToID("_FlowB");
        private static readonly int FlowAPrevId = Shader.PropertyToID("_FlowAPrev");
        private static readonly int FlowLerpId = Shader.PropertyToID("_FlowLerp");
        private static readonly int FlowWeightsId = Shader.PropertyToID("_FlowWeights");
        private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
        private static readonly int DetailErodeId = Shader.PropertyToID("_DetailErode");
        private static readonly int DetailStreakId = Shader.PropertyToID("_DetailStreak");
        private static readonly int GasFillId = Shader.PropertyToID("_GasFill");
        private static readonly int StretchFineId = Shader.PropertyToID("_StretchFine");
        private static readonly int DitherFrameId = Shader.PropertyToID("_DitherFrame");
        private static readonly int CrinkleId = Shader.PropertyToID("_Crinkle");
        private static readonly int SparseId = Shader.PropertyToID("_Sparse");
        private static readonly int PulseMeanId = Shader.PropertyToID("_PulseMean");
        private static readonly int RootOffsetId = Shader.PropertyToID("_RootOffset");
        private static readonly int ArchLineId = Shader.PropertyToID("_ArchLine");
        private static readonly int JetsId = Shader.PropertyToID("_Jets");
        private static readonly int JetWaysId = Shader.PropertyToID("_JetWays");
        private static readonly int JetCountId = Shader.PropertyToID("_JetCount");
        private static readonly int JetShapeId = Shader.PropertyToID("_JetShape");
        private static readonly int JetBendId = Shader.PropertyToID("_JetBend");
        private static readonly int JetClearId = Shader.PropertyToID("_JetClear");
        private static readonly int ArchFlameId = Shader.PropertyToID("_ArchFlame");
        private static readonly int ArchBendId = Shader.PropertyToID("_ArchBend");
        private static readonly int ArchFlowId = Shader.PropertyToID("_ArchFlow");

        // The values that change only when the test bed tunes them: set once,
        // and again after each tuning, not every frame (audit V7, 2026-11-09).
        private void SetConstants()
        {
            // The brick arch, where the engine has one (BrickArch): where its
            // bricks are in the box, and how the flames go under and round them.
            archFitted = Arch != null && Arch.Fitted;
            if (archFitted && BrickArch.ArchShown > 0.5f)
            {
                material.SetVector(ArchLineId, Arch.Line);
                material.SetVector(ArchFlameId, new Vector4(Mathf.Clamp(BrickArch.ArchLayer, 0.05f, 0.95f), BrickArch.ArchBack, BrickArch.ArchOver, Arch.LipAlongBed));
                material.SetVector(ArchBendId, new Vector4(Mathf.Max(1f, BrickArch.ArchBend), BrickArch.ArchDrag, BrickArch.ArchTurn, BrickArch.ArchRoll));
#if PUBLIC_RELEASE
                material.SetVector(ArchFlowId, new Vector4(BrickArch.ArchBlur, 0f, Mathf.Clamp(BrickArch.ArchSqueeze, 0.05f, 1f), Mathf.Max(0f, BrickArch.ArchCarry)));
#else
                material.SetVector(ArchFlowId, new Vector4(BrickArch.ArchBlur, FireboxTestBed.Enabled ? BrickArch.ArchAlone : 0f, Mathf.Clamp(BrickArch.ArchSqueeze, 0.05f, 1f), Mathf.Max(0f, BrickArch.ArchCarry)));
#endif
            }
            else
                material.SetVector(ArchFlameId, Vector4.zero);
            material.SetFloat(RootsId, Roots);
            material.SetFloat(BeatGlowId, BeatGlow);
            material.SetFloat(BeatHeightId, BeatHeight);
            material.SetFloat(BeatHeatId, BeatHeat);
            material.SetFloat(BeatCoverId, BeatCover);
            material.SetFloat(LickHeightId, LickHeight);
            material.SetFloat(LickDepthId, CoalLumps.UnderDrop);
            material.SetFloat(LickScaleId, LickScale);
            material.SetFloat(LickLowId, LickLow);
            material.SetFloat(LickStrengthId, LickStrength);
            material.SetFloat(LickGlowId, LickGlow);
            material.SetFloat(LickHeatId, LickHeat);
            material.SetFloat(LickStandingId, Mathf.Clamp01(LickStanding));
            material.SetFloat(LickEdgeSharpnessId, Mathf.Clamp01(LickEdgeSharpness));
            material.SetFloat(LickDetailId, Mathf.Clamp01(LickDetail));
            material.SetFloat(LickCurlId, Mathf.Max(0f, LickCurl));
            material.SetFloat(LickRoughnessId, Mathf.Max(0f, LickRoughness));
            material.SetFloat(DoorSlopeId, ZonedCoalBed.DoorSlope);
            material.SetFloat(SideWidthId, SideWidth);
            material.SetFloat(SideSwirlId, SideSwirl);
            material.SetFloat(DetailScaleId, DetailScale);
            material.SetFloat(DetailErodeId, DetailErode);
            material.SetFloat(DetailStreakId, DetailStreak);
            material.SetFloat(GasFillId, GasFill);
            material.SetFloat(StretchFineId, StretchFine);
            material.SetFloat(CrinkleId, Crinkle);
            material.SetFloat(SparseId, Sparse);
            material.SetFloat(SwayId, Sway);
            material.SetFloat(DetailFlowCarryId, Mathf.Clamp01(DetailFlowCarry));
            material.SetFloat(BuoyancyId, Buoyancy);
            material.SetFloat(BaseHeightId, BaseHeight);
            material.SetFloat(SpotLowId, Mathf.Clamp(SpotLow, 0f, 0.95f));
            material.SetFloat(TipHeatId, TipHeat);
            material.SetFloat(GlowId, Glow);
            material.SetFloat(FlameDensityId, FlameDensity);
            material.SetFloat(SmokeAlbedoId, SmokeAlbedo);
            material.SetFloat(SmokeLightId, SmokeLight);
            material.SetFloat(SharpId, Sharp);
            material.SetFloat(EdgeMeanId, Mathf.Clamp01(EdgeMean));
            material.SetFloat(SmokeLocalId, Mathf.Clamp01(SmokeLocal));
            material.SetFloat(DetailBlurId, Mathf.Clamp(DetailBlur, 0f, 1.5f));
            material.SetFloat(DitherAmountId, Mathf.Clamp01(DitherAmount));
            material.SetFloat(DitherShiftId, Mathf.Clamp01(DitherShift));
            material.SetFloat(SmokePlumeId, Mathf.Max(0f, SmokePlume));
            material.SetFloat(SmokeStrideId, Mathf.Clamp(SmokeStride, 1f, 6f));
            material.SetFloat(NoiseTileId, NoiseTile);
            material.SetFloat(SheetLowId, SheetLow);
            material.SetFloat(SheetHighId, SheetHigh);
            material.SetFloat(CarpetId, Carpet);
            material.SetFloat(StretchId, Stretch);
            material.SetFloat(MixSheetsId, MixSheets);
            material.SetFloat(MixCurlsId, MixCurls);
            material.SetFloat(MixLobesId, MixLobes);
            material.SetFloat(HeatScaleId, HeatScale);
            material.SetFloat(FlameHeatId, FlameHeat);
            material.SetFloat(EdgeHeatId, EdgeHeat);
            material.SetFloat(CoreDepthId, CoreDepth);
        }
    }
}
