using BepInEx;
using MonoMod.Cil;
using NS_KingKombatArena;
using R2API;
using R2API.Utils;
using RoR2;
using System;
using UnityEngine;
using UnityEngine.Networking;

namespace KingKombatArena_Tweaks
{
    [BepInDependency("com.bepis.r2api")]
    [BepInDependency("com.Kingpinush.KingKombatArena", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("com.Moffein.KingKombatArena_Tweaks", "KingKombatArena Tweaks", "1.0.0")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [R2API.Utils.R2APISubmoduleDependency(nameof(RecalculateStatsAPI))]
    public class KingKombatArena_Tweaks : BaseUnityPlugin
    {
        public static bool weakerStun = true;
        public static bool allowChainStun = false;
        public static bool noIFrames = true;
        public static float iFrameArmor = 200f;
        public static float damageMult = 1f;
        public static bool disableMicrobots = true;

        public static bool duelActive = false;

        public void Awake()
        {
            ReadConfig();
            On.RoR2.Run.Start += (orig, self) =>
            {
                orig(self);
                duelActive = false;
                self.gameObject.AddComponent<KombatArenaTracker>();
            };

            if (weakerStun && NS_KingKombatArena.Configuration.EnableStunsAndShocksDuringDuels.Value)
            {
                On.RoR2.HealthComponent.TakeDamage += (orig, self, damageInfo) =>
                {
                    bool pendingStun = false;
                    bool pendingShock = false;
                    if (NetworkServer.active && duelActive)
                    {
                        if ((damageInfo.damageType & DamageType.Stun1s) > 0)
                        {
                            damageInfo.damageType &= ~DamageType.Stun1s;
                            pendingStun = true;
                        }
                        if ((damageInfo.damageType & DamageType.Shock5s) > 0)
                        {
                            damageInfo.damageType &= ~DamageType.Shock5s;
                            pendingShock = true;
                        }
                    }

                    orig(self, damageInfo);

                    if (NetworkServer.active && !damageInfo.rejected && self.alive && self.body
                    && (allowChainStun || !self.body.HasBuff(RoR2Content.Buffs.LunarSecondaryRoot)))
                    {
                        if (pendingStun)
                        {
                            self.body.AddTimedBuff(RoR2Content.Buffs.LunarSecondaryRoot, 1f);
                        }
                        if (pendingShock)
                        {
                            self.body.AddTimedBuff(RoR2Content.Buffs.LunarSecondaryRoot, 3f);
                        }
                    }
                };

                On.RoR2.SetStateOnHurt.SetStun += (orig, self, duration) =>
                {
                    if (duelActive)
                    {
                        CharacterBody cb = self.gameObject.GetComponent<CharacterBody>();
                        if (cb && (allowChainStun || !cb.HasBuff(RoR2Content.Buffs.LunarSecondaryRoot)))
                        {
                            cb.AddTimedBuff(RoR2Content.Buffs.LunarSecondaryRoot, duration);
                            return;
                        }
                    }
                    orig(self, duration);
                };
            }
            if (noIFrames)
            {
                //Disabe vanilla iframes while dueling
                IL.RoR2.HealthComponent.TakeDamage += (il) =>
                {
                    ILCursor c = new ILCursor(il);
                    c.GotoNext(
                         x => x.MatchLdsfld(typeof(RoR2Content.Buffs), "HiddenInvincibility")
                        );
                    c.Index += 2;
                    c.EmitDelegate<Func<bool, bool>>((orig) =>
                    {
                        return orig && !duelActive;
                    });
                };

                RecalculateStatsAPI.GetStatCoefficients += (sender, args) =>
                {
                    if (duelActive && sender.HasBuff(RoR2Content.Buffs.HiddenInvincibility))
                    {
                        args.armorAdd += iFrameArmor;
                    }
                };
            }
            if (disableMicrobots)
            {
                On.EntityStates.CaptainDefenseMatrixItem.DefenseMatrixOn.DeleteNearbyProjectile += (orig, self) =>
                {
                    if (!duelActive)
                    {
                        return orig(self);
                    }
                    return false;
                };
            }
            On.RoR2.CharacterBody.RecalculateStats += (orig, self) =>
            {
                orig(self);
                if (duelActive)
                {
                    self.damage *= damageMult;

                    if (self.HasBuff(RoR2Content.Buffs.Immune) && self.healthComponent)
                    {
                        self.healthComponent.health = self.maxHealth;
                        self.healthComponent.shield = self.healthComponent.fullShield;
                    }
                }
            };
        }

        private void ReadConfig()
        {

        }
    }

    public class KombatArenaTracker : MonoBehaviour
    {
        float stopwatch = 0f;
        public void FixedUpdate()
        {
            stopwatch += Time.fixedDeltaTime;
            if (stopwatch > 1f)
            {
                stopwatch -= 1f;
                KombatArenaInstance kai = KingKombatArenaMainPlugin.AccessCurrentKombatArenaInstance();
                if (kai != null)
                {
                    bool oldDuelActive = KingKombatArena_Tweaks.duelActive;
                    KingKombatArena_Tweaks.duelActive = kai.GetKombatArenaStateMachine().IsInState(typeof(DuelCombatState));

                    if (oldDuelActive && !KingKombatArena_Tweaks.duelActive)
                    {
                        foreach (CharacterMaster cm in CharacterMaster.instancesList)
                        {
                            GameObject bodyObject = cm.GetBodyObject();
                            if (bodyObject)
                            {
                                DotController.RemoveAllDots(bodyObject);    //Hopefully this stops thallium
                            }
                        }
                    }
                }
            }
        }
    }
}
