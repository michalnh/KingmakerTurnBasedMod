﻿using Harmony12;
using Kingmaker.Controllers;
using Kingmaker.Controllers.Clicks.Handlers;
using Kingmaker.Controllers.Combat;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.SettingsUI;
using Kingmaker.UnitLogic.Commands;
using ModMaker.Utility;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using TurnBased.Utility;
using UnityEngine;
using static ModMaker.Utility.ReflectionCache;
using static TurnBased.Utility.SettingsWrapper;
using static TurnBased.Utility.StatusWrapper;

namespace TurnBased.HarmonyPatches
{
    static class Misc
    {
        // toggle 5-foot step when right click on the ground
        [HarmonyPatch(typeof(ClickGroundHandler), nameof(ClickGroundHandler.OnClick), typeof(GameObject), typeof(Vector3), typeof(int))]
        static class ClickGroundHandler_OnClick_Patch
        {
            [HarmonyPrefix]
            static bool Prefix(int button)
            {
                if (IsInCombat() && ToggleFiveFootStepOnRightClickGround && button == 1)
                {
                    CurrentTurn()?.CommandToggleFiveFootStep();
                    return false;
                }
                return true;
            }
        }

        // speed up iterative attacks
        [HarmonyPatch(typeof(UnitAttack), "OnTick")]
        static class UnitAttack_OnTick_Patch
        {
            [HarmonyTranspiler]
            static IEnumerable<CodeInstruction> Transpiler(MethodBase original, IEnumerable<CodeInstruction> codes, ILGenerator il)
            {
                // ---------------- before ----------------
                // m_AnimationsDuration / (float)m_AllAttacks.Count
                // ---------------- after  ----------------
                // ModifyDelay(m_AnimationsDuration / (float)m_AllAttacks.Count)
                List<CodeInstruction> findingCodes = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld,
                        GetFieldInfo<UnitAttack, float>("m_AnimationsDuration")),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld,
                        GetFieldInfo<UnitAttack, List<AttackHandInfo>>("m_AllAttacks")),
                    new CodeInstruction(OpCodes.Callvirt,
                        GetPropertyInfo<List<AttackHandInfo>, int>(nameof(List<AttackHandInfo>.Count)).GetGetMethod(true)),
                    new CodeInstruction(OpCodes.Conv_R4),
                    new CodeInstruction(OpCodes.Div),
               };
                int startIndex = codes.FindCodes(findingCodes);
                if (startIndex >= 0)
                {
                    return codes.Insert(startIndex + findingCodes.Count, new CodeInstruction(OpCodes.Call,
                        new Func<float, float>(ModifyDelay).Method), true).Complete();
                }
                else
                {
                    throw new Exception($"Failed to patch '{MethodBase.GetCurrentMethod().DeclaringType}'");
                }
            }

            static float ModifyDelay(float delay)
            {
                return IsInCombat() && delay > MaxDelayBetweenIterativeAttacks ? MaxDelayBetweenIterativeAttacks : delay;
            }
        }

        // speed up casting
        [HarmonyPatch(typeof(UnitUseAbility), nameof(UnitUseAbility.Init), typeof(UnitEntityData))]
        static class UnitUseAbility_Init_Patch
        {
            [HarmonyPostfix]
            static void Postfix(UnitUseAbility __instance)
            {
                if (IsInCombat() && __instance.Executor.IsInCombat && CastingTimeOfFullRoundSpell != 1f)
                {
                    float castTime = __instance.GetCastTime();
                    if (castTime >= 6f)
                    {
                        __instance.SetCastTime(castTime * CastingTimeOfFullRoundSpell);
                    }
                }
            }
        }

        // make flanking don't consider opponents' command anymore
        [HarmonyPatch(typeof(UnitCombatState), nameof(UnitCombatState.IsFlanked), MethodType.Getter)]
        static class UnitCombatState_get_IsFlanked_Patch
        {
            [HarmonyPrefix]
            static bool Prefix(UnitCombatState __instance, ref bool __result)
            {
                if (IsInCombat() && __instance.Unit.IsInCombat && FlankingCountAllNearbyOpponents)
                {
                    __result = __instance.EngagedBy.Count > 1 && !__instance.Unit.Descriptor.State.Features.CannotBeFlanked;
                    return false;
                }
                return true;
            }
        }

        // suppress auto pause on combat start
        [HarmonyPatch(typeof(AutoPauseController), nameof(AutoPauseController.HandlePartyCombatStateChanged), typeof(bool))]
        static class AutoPauseController_HandlePartyCombatStateChanged_Patch
        {
            [HarmonyPrefix]
            static void Prefix(UnitCombatState __instance, bool inCombat, ref bool? __state)
            {
                if (IsEnabled() && DoNotPauseOnCombatStart && inCombat)
                {
                    __state = SettingsRoot.Instance.PauseOnEngagement.CurrentValue;
                    SettingsRoot.Instance.PauseOnEngagement.CurrentValue = false;
                }
            }

            [HarmonyPostfix]
            static void Postfix(UnitCombatState __instance, ref bool? __state)
            {
                if (__state.HasValue)
                {
                    SettingsRoot.Instance.PauseOnEngagement.CurrentValue = __state.Value;
                }
            }
        }
    }
}