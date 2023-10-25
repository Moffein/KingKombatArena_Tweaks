using BepInEx;
using BepInEx.Configuration;
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
    [BepInDependency("com.Kingpinush.KingKombatArena", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("com.Moffein.KingKombatArena_Tweaks", "KingKombatArena Tweaks", "1.0.2")]
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

                    //Fix heal mult config option giving low HP on duel start.
                    /*if (self.HasBuff(RoR2Content.Buffs.Immune) && self.healthComponent)
                    {
                        self.healthComponent.health = self.maxHealth;
                        self.healthComponent.shield = self.healthComponent.fullShield;
                    }*/
                }
            };
        }

        private void ReadConfig()
        {
            weakerStun = base.Config.Bind<bool>(new ConfigDefinition("Stuns", "Weaker Stuns"), true, new ConfigDescription("Players can still attack during stuns.")).Value;
            allowChainStun = base.Config.Bind<bool>(new ConfigDefinition("Stuns", "Allow Chain Stuns"), false, new ConfigDescription("Allow players to be re-stunned while in a stun (requires Weaker Stuns).")).Value;

            noIFrames = base.Config.Bind<bool>(new ConfigDefinition("I-Frames", "Disable I-Frames"), true, new ConfigDescription("I-Frames give armor instead.")).Value;
            iFrameArmor = base.Config.Bind<float>(new ConfigDefinition("I-Frames", "I-Frame Armor"), 200f, new ConfigDescription("Armor to give if I-Frames are disabled.")).Value;

            disableMicrobots = base.Config.Bind<bool>(new ConfigDefinition("Captain", "Disable Defensive Microbots"), true, new ConfigDescription("Disables Defensive Microbots during duels.")).Value;
        
            damageMult = base.Config.Bind<float>(new ConfigDefinition("Stats", "Damage Multiplier"), 1f, new ConfigDescription("Multiplies player damage during duels.")).Value;
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
