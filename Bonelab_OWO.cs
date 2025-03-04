﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using MyOwoVest;
using Il2Cpp;

[assembly: MelonInfo(typeof(Bonelab_OWO.Bonelab_OWO), "Bonelab_OWO", "2.0.0", "Florian Fahrenberger")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace Bonelab_OWO
{
    public class Bonelab_OWO : MelonMod
    {
        public static TactsuitVR tactsuitVr = null!;
        public static bool playerRightHanded = true;
        public static Stopwatch heartTimer = new Stopwatch();


        public override void OnInitializeMelon()
        {
            tactsuitVr = new TactsuitVR();
            
        }

        [HarmonyPatch(typeof(Il2CppSLZ.Props.Weapons.Gun), "Fire", new Type[] { })]
        public class bhaptics_FireGun
        {
            [HarmonyPostfix]
            public static void Postfix(Il2CppSLZ.Props.Weapons.Gun __instance)
            {
                bool rightHanded = false;
                bool twoHanded = false;
                bool supportHand = __instance._isSlideGrabbed;

                if (__instance == null) return;
                if (__instance.triggerGrip == null) return;
                if (__instance.triggerGrip.attachedHands == null) return;
                try { if (__instance.AmmoCount() <= 0) return; }
                catch (NullReferenceException) { tactsuitVr.LOG("NullReference in AmmoCount."); return; }
                twoHanded = (__instance.triggerGrip.attachedHands.Count > 1);
                
                foreach (var myHand in __instance.triggerGrip.attachedHands)
                {
                    if (myHand.handedness == Il2CppSLZ.Handedness.RIGHT) rightHanded = true;
                }

                if (__instance.otherGrips != null)
                {
                    foreach (var myGrip in __instance.otherGrips)
                    {
                        if (myGrip.attachedHands.Count > 0)
                        {
                            foreach (var myHand in myGrip.attachedHands)
                            {
                                if ((myHand.handedness == Il2CppSLZ.Handedness.LEFT) && (rightHanded)) supportHand = true;
                                if ((myHand.handedness == Il2CppSLZ.Handedness.RIGHT) && (!rightHanded)) supportHand = true;
                            }
                        }
                    }
                }

                //tactsuitVr.LOG("Kickforce: " + __instance.kickForce.ToString());
                float intensity = __instance.kickForce / 12.0f;

                tactsuitVr.GunRecoil(rightHanded, intensity, twoHanded, supportHand);
            }
        }

        private static KeyValuePair<float, float> getAngleAndShift(Transform player, Vector3 hit)
        {
            // bhaptics pattern starts in the front, then rotates to the left. 0° is front, 90° is left, 270° is right.
            // y is "up", z is "forward" in local coordinates
            Vector3 patternOrigin = new Vector3(0f, 0f, 1f);
            Vector3 hitPosition = hit - player.position;
            Quaternion myPlayerRotation = player.rotation;
            Vector3 playerDir = myPlayerRotation.eulerAngles;
            // get rid of the up/down component to analyze xz-rotation
            Vector3 flattenedHit = new Vector3(hitPosition.x, 0f, hitPosition.z);

            // get angle. .Net < 4.0 does not have a "SignedAngle" function...
            float hitAngle = Vector3.Angle(flattenedHit, patternOrigin);
            // check if cross product points up or down, to make signed angle myself
            Vector3 crossProduct = Vector3.Cross(flattenedHit, patternOrigin);
            if (crossProduct.y < 0f) { hitAngle *= -1f; }
            // relative to player direction
            float myRotation = hitAngle - playerDir.y;
            // switch directions (bhaptics angles are in mathematically negative direction)
            myRotation *= -1f;
            // convert signed angle into [0, 360] rotation
            if (myRotation < 0f) { myRotation = 360f + myRotation; }


            // up/down shift is in y-direction
            // in Battle Sister, the torso Transform has y=0 at the neck,
            // and the torso ends at roughly -0.5 (that's in meters)
            // so cap the shift to [-0.5, 0]...
            float hitShift = hitPosition.y;
            //tactsuitVr.LOG("HitShift: " + hitShift.ToString());
            float upperBound = 0.5f;
            float lowerBound = -0.5f;
            if (hitShift > upperBound) { hitShift = 0.5f; }
            else if (hitShift < lowerBound) { hitShift = -0.5f; }
            // ...and then spread/shift it to [-0.5, 0.5], which is how bhaptics expects it
            else { hitShift = (hitShift - lowerBound) / (upperBound - lowerBound) - 0.5f; }

            // No tuple returns available in .NET < 4.0, so this is the easiest quickfix
            return new KeyValuePair<float, float>(myRotation, hitShift);
        }

        /*
        [HarmonyPatch(typeof(Player_Health), "TAKEDAMAGE", new Type[] { typeof(float) })]
        public class bhaptics_TakeDamage
        {
            [HarmonyPostfix]
            public static void Postfix(Player_Health __instance, float damage)
            {
                tactsuitVr.LOG("PlayerHealth damage: " + __instance.curr_Health.ToString() + " " + damage.ToString());
            }
        }
        */

        [HarmonyPatch(typeof(PlayerDamageReceiver), "ReceiveAttack", new Type[] { typeof(Il2CppSLZ.Combat.Attack) })]
        public class bhaptics_ReceiveAttack
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerDamageReceiver __instance, Il2CppSLZ.Combat.Attack attack)
            {
                float armDamage = 0.2f;
                float headDamage = 0.8f;
                float bodyDamage = 0.5f;
                string damagePattern;
                bool hapticsApplied = false;
                switch (attack.attackType)
                {
                    case Il2CppSLZ.Marrow.Data.AttackType.Piercing:
                        damagePattern = "HitShot";
                        break;
                    case Il2CppSLZ.Marrow.Data.AttackType.Blunt:
                        damagePattern = "HitDefault";
                        break;
                    case Il2CppSLZ.Marrow.Data.AttackType.Electric:
                        damagePattern = "HitElectric";
                        break;
                    case Il2CppSLZ.Marrow.Data.AttackType.Explosive:
                        damagePattern = "Explosion";
                        break;
                    case Il2CppSLZ.Marrow.Data.AttackType.Fire:
                        damagePattern = "HitFire";
                        break;
                    case Il2CppSLZ.Marrow.Data.AttackType.Ice:
                        damagePattern = "HitFreeze";
                        break;
                    case Il2CppSLZ.Marrow.Data.AttackType.Slicing:
                        damagePattern = "HitBlade";
                        break;
                    case Il2CppSLZ.Marrow.Data.AttackType.Stabbing:
                        damagePattern = "HitShot";
                        break;
                    default:
                        damagePattern = "HitDefault";
                        break;
                }
                float absoluteDamage = Math.Abs(attack.damage);
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.Head)
                {
                    absoluteDamage *= headDamage;
                }
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.LeftArm)
                {
                    tactsuitVr.ArmHit(damagePattern, false);
                    hapticsApplied = true;
                    absoluteDamage *= armDamage;
                }
                if (__instance.bodyPart == PlayerDamageReceiver.BodyPart.RightArm)
                {
                    tactsuitVr.ArmHit(damagePattern, true);
                    hapticsApplied = true;
                    absoluteDamage *= armDamage;
                }

                if ((!hapticsApplied) && (attack.collider != null))
                {
                    var angleShift = getAngleAndShift(__instance.transform, attack.collider.transform.position);
                    tactsuitVr.PlayBackHit(damagePattern, angleShift.Key, angleShift.Value);
                    absoluteDamage *= bodyDamage;
                }
                __instance.health.TAKEDAMAGE(absoluteDamage);
            }
        }

        
        [HarmonyPatch(typeof(Il2CppSLZ.Interaction.InventorySlotReceiver), "OnHandGrab", new Type[] { typeof(Il2CppSLZ.Interaction.Hand) })]
        public class bhaptics_SlotGrab
        {
            [HarmonyPostfix]
            public static void Postfix(Il2CppSLZ.Interaction.InventorySlotReceiver __instance, Il2CppSLZ.Interaction.Hand hand)
            {
                if (__instance.isInUIMode) return;
                if (hand == null) return;
                bool rightHand = (hand.handedness == Il2CppSLZ.Handedness.RIGHT);
                if (__instance.slotType == Il2CppSLZ.Props.Weapons.WeaponSlot.SlotType.SIDEARM)
                {
                    if (rightHand) tactsuitVr.PlayBackFeedback("GrabGun_L");
                    else tactsuitVr.PlayBackFeedback("GrabGun_R");
                }
                else
                {
                    if (rightHand) tactsuitVr.PlayBackFeedback("ReceiveShoulder_R");
                    else tactsuitVr.PlayBackFeedback("ReceiveShoulder_L");
                }
            }
        }

        [HarmonyPatch(typeof(Il2CppSLZ.Interaction.InventorySlotReceiver), "OnHandDrop", new Type[] { typeof(Il2CppSLZ.Interaction.IGrippable) })]
        public class bhaptics_SlotInsert
        {
            [HarmonyPostfix]
            public static void Postfix(Il2CppSLZ.Interaction.InventorySlotReceiver __instance, Il2CppSLZ.Interaction.IGrippable host)
            {
                if (__instance == null) return;
                if (__instance.isInUIMode) return;
                if (host == null) return;
                Il2CppSLZ.Interaction.Hand hand = host.GetLastHand();
                if (hand == null) return;
                bool rightHand = (hand.handedness == Il2CppSLZ.Handedness.RIGHT);
                if (__instance.slotType == Il2CppSLZ.Props.Weapons.WeaponSlot.SlotType.SIDEARM)
                {
                    if (rightHand) tactsuitVr.PlayBackFeedback("StoreGun_L");
                    else tactsuitVr.PlayBackFeedback("StoreGun_R");
                }
                else
                {
                    if (rightHand) tactsuitVr.PlayBackFeedback("StoreShoulder_R");
                    else tactsuitVr.PlayBackFeedback("StoreShoulder_L");
                }
            }
        }

        /*
        [HarmonyPatch(typeof(BaseGameController), "OnSlowTime", new Type[] { typeof(float) })]
        public class bhaptics_SlowTime
        {
            [HarmonyPostfix]
            public static void Postfix(BaseGameController __instance)
            {
                tactsuitVr.PlayBackFeedback("SloMo");
            }
        }
        */

        [HarmonyPatch(typeof(Il2CppSLZ.SaveData.PlayerSettings), "OnPropertyChanged", new Type[] { typeof(string) })]
        public class bhaptics_PropertyChanged
        {
            [HarmonyPostfix]
            public static void Postfix(Il2CppSLZ.SaveData.PlayerSettings __instance)
            {
                playerRightHanded = __instance.RightHanded;
            }
        }


        
        [HarmonyPatch(typeof(Player_Health), "Update", new Type[] { })]
        public class bhaptics_PlayerHealthUpdate
        {
            [HarmonyPostfix]
            public static void Postfix(Player_Health __instance)
            {
                if (__instance.curr_Health <= 0.3f * __instance.max_Health)
                {
                    if (!heartTimer.IsRunning)
                    {
                        heartTimer.Start();
                        tactsuitVr.PlayBackFeedback("ThreeHeartBeats");
                        return;
                    }
                    if (heartTimer.ElapsedMilliseconds <= 2000) return;
                    heartTimer.Restart();
                    tactsuitVr.PlayBackFeedback("ThreeHeartBeats");
                }
            }
        }
        
        [HarmonyPatch(typeof(Il2CppSLZ.Props.PullCordDevice), "SwapAvatar", new Type[] { typeof(Il2CppSLZ.Marrow.Warehouse.AvatarCrateReference) })]
        public class bhaptics_SwapAvatar
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                tactsuitVr.PlayBackFeedback("SwitchAvatar");
            }
        }

    }
}
