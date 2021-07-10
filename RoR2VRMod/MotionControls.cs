﻿using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace VRMod
{
    public static class MotionControls
    {
        public static bool HandsReady => dominantHand != null && nonDominantHand != null && dominantHand.currentHand != null && nonDominantHand.currentHand != null;

        private static GameObject handSelectorPrefab;

        private static HandController dominantHand;
        private static HandController nonDominantHand;

        private static List<GameObject> handPrefabs = new List<GameObject>();

        private static Image cachedSprintIcon;
        private static Color originalSprintIconColor;

        public delegate void SetHandPairEventHandler(CharacterBody body);

        public static SetHandPairEventHandler onHandPairSet;

        internal delegate void SkinAppliedEventHandler();

        internal static SkinAppliedEventHandler onSkinApplied;

        internal static CharacterBody currentBody;

        private static List<HUDQueueEntry> wristHudQueue = new List<HUDQueueEntry>();

        private static List<HUDQueueEntry> watchHudQueue = new List<HUDQueueEntry>();

        internal struct HUDQueueEntry
        {
            internal RectTransform transform;
            internal bool left;

            internal HUDQueueEntry(RectTransform transform, bool left)
            {
                this.transform = transform;
                this.left = left;
            }
        }

        internal static void Init()
        {
            On.RoR2.CameraRigController.Start += InitWristHUD;

            On.RoR2.CharacterBody.OnSprintStart += OnSprintStart;
            On.RoR2.CharacterBody.OnSprintStop += OnSprintStop;

            //IL.RoR2.CharacterMaster.OnInventoryChanged += TransformHereticHandsIL;

            On.RoR2.CharacterModel.UpdateMaterials += UpdateHandMaterials;

            PlayerCharacterMasterController.onPlayerAdded += (pcmc) =>
            {
                pcmc.master.onBodyStart += (body) =>
                {
                    if (body.master != LocalUserManager.GetFirstLocalUser().cachedMaster) return;

                    currentBody = body;
                    string bodyName = body.name.Substring(0, body.name.IndexOf("(Clone)"));
                    VRMod.StaticLogger.LogInfo(String.Format("Local cached body \'{0}\' found. Applying hand pair.", bodyName));

                    SetHandPair(body);
                };
            };

            On.RoR2.ModelSkinController.ApplySkin += (orig, self, index) =>
            {
                orig(self, index);
                if (self.characterModel.body == currentBody && onSkinApplied != null)
                {
                    onSkinApplied();
                }
            };

            handSelectorPrefab = VRMod.VRAssetBundle.LoadAsset<GameObject>("VRHand");

            string[] prefabNames = new string[]
            {
                "CommandoPistol",
                "HuntressBow",
                "HuntressHand",
                "BanditRifle",
                "BanditHand",
                "MULTTools",
                "MULTTools2",
                "EngiHand",
                "EngiHand2",
                "ArtiHand",
                "ArtiHand2",
                "MercHand",
                "MercSword",
                "RexGun",
                "RexFlower",
                "LoaderHand",
                "LoaderHand2",
                "AcridHand",
                "AcridHand2",
                "CaptainHand",
                "CaptainGun",
                "HereticWing"
            };

            foreach (string prefabName in prefabNames)
            {
                GameObject prefab = VRMod.VRAssetBundle.LoadAsset<GameObject>(prefabName);

                if (prefabName == "BanditRifle")
                {
                    TwoHandedMainHand origin = prefab.GetComponent<TwoHandedMainHand>();

                    origin.snapAngle = ModConfig.BanditWeaponGripSnapAngle.Value;
                }
                if (prefabName == "MercSword")
                {
                    MeleeSkill melee = prefab.GetComponent<MeleeSkill>();

                    melee.speedThreshold = ModConfig.MercSwingSpeedThreshold.Value;
                }
                if (prefabName == "LoaderHand" || prefabName == "LoaderHand2")
                {
                    MeleeSkill melee = prefab.GetComponent<MeleeSkill>();

                    melee.speedThreshold = ModConfig.LoaderSwingSpeedThreshold.Value;
                }
                if (prefabName == "AcridHand" || prefabName == "AcridHand2")
                {
                    MeleeSkill melee = prefab.GetComponent<MeleeSkill>();

                    melee.speedThreshold = ModConfig.AcridSwingSpeedThreshold.Value;
                }

                if (prefab)
                {
                    AddHandPrefab(prefab);
                }
            }
        }

        private static void TransformHereticHandsIL(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            c.GotoNext(
                x => x.MatchCall(typeof(CharacterMaster), "TransformBody")
            );

            c.Index++;

            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Action<CharacterMaster>>((cm) =>
            {
                if (cm == LocalUserManager.GetFirstLocalUser().cachedMaster)
                {
                    RoR2Application.onFixedUpdate += CheckForLocalBody;
                }
            });
        }

        internal static void SetSprintIcon(Image sprintIcon)
        {
            cachedSprintIcon = sprintIcon;
        }

        private static void OnSprintStop(On.RoR2.CharacterBody.orig_OnSprintStop orig, CharacterBody self)
        {
            if (self == LocalUserManager.GetFirstLocalUser().cachedBody)
            {
                if (cachedSprintIcon)
                    cachedSprintIcon.color = originalSprintIconColor;
            }

            if (self.name.Contains("Bandit2"))
            {
                GetHandByDominance(true).animator.SetBool("IsSprinting", false);
            }

            orig(self);
        }

        private static void OnSprintStart(On.RoR2.CharacterBody.orig_OnSprintStart orig, CharacterBody self)
        {
            if (self == LocalUserManager.GetFirstLocalUser().cachedBody)
            {
                if (!cachedSprintIcon)
                {
                    Transform iconTransform = LocalUserManager.GetFirstLocalUser().cameraRigController.hud.mainUIPanel.transform.Find("SpringCanvas/BottomRightCluster/Scaler/SprintCluster/SprintIcon");
                    if (iconTransform)
                    {
                        Image sprintIcon = iconTransform.GetComponent<Image>();

                        if (sprintIcon)
                            cachedSprintIcon = sprintIcon;
                    }
                }

                if (cachedSprintIcon)
                {
                    originalSprintIconColor = cachedSprintIcon.color;

                    cachedSprintIcon.color = Color.yellow;
                }
            }

            if (self.name.Contains("Bandit2"))
            {
                GetHandByDominance(true).animator.SetBool("IsSprinting", true);
            }

            orig(self);
        }

        private static void UpdateHandMaterials(On.RoR2.CharacterModel.orig_UpdateMaterials orig, CharacterModel self)
        {
            orig(self);

            if (!HandsReady) return;

            LocalUser localUser = LocalUserManager.GetFirstLocalUser();

            if (localUser != null && self.body == localUser.cachedBody && self.visibility != VisibilityLevel.Invisible)
            {
                foreach (CharacterModel.RendererInfo rendererInfo in dominantHand.currentHand.rendererInfos)
                {
                    if (!rendererInfo.renderer || !rendererInfo.defaultMaterial) continue;
                    self.UpdateRendererMaterials(rendererInfo.renderer, rendererInfo.defaultMaterial, rendererInfo.ignoreOverlays);
                }

                foreach (CharacterModel.RendererInfo rendererInfo in nonDominantHand.currentHand.rendererInfos)
                {
                    if (!rendererInfo.renderer || !rendererInfo.defaultMaterial) continue;
                    self.UpdateRendererMaterials(rendererInfo.renderer, rendererInfo.defaultMaterial, rendererInfo.ignoreOverlays);
                }
            }
        }

        internal static void AddWristHUD(bool left, RectTransform hudCluster)
        {
            if (HandsReady)
                (left == ModConfig.LeftDominantHand.Value ? dominantHand : nonDominantHand).smallHud.AddHUDCluster(hudCluster);
            else
                wristHudQueue.Add(new HUDQueueEntry(hudCluster, left));
        }

        internal static void AddWatchHUD(bool left, RectTransform hudCluster)
        {
            if (HandsReady)
                (left == ModConfig.LeftDominantHand.Value ? dominantHand : nonDominantHand).watchHud.AddHUDCluster(hudCluster);
            else
                watchHudQueue.Add(new HUDQueueEntry(hudCluster, left));
        }

        /// <summary>
        /// Adds a hand prefab to use when the associated character is being played in VR. This must only be called once per prefab.
        /// </summary>
        /// <param name="handPrefab">The hand prefab containing a hand script.</param>
        public static void AddHandPrefab(GameObject handPrefab)
        {
            if (!handPrefab)
            {
                throw new ArgumentNullException("VR Mod: Cannot add null as a hand prefab.");
            }

            Hand handComponent = handPrefab.GetComponent<Hand>();

            if (!handComponent)
            {
                throw new ArgumentException("VR Mod: The prefab is missing a Hand component and cannot be added as a hand prefab.");
            }

            if (handPrefabs.Exists((x) => CheckPrefabMatch(handComponent, x)))
            {
                throw new ArgumentException("VR Mod: A hand with the same body name and hand type has already been added. Make sure to only add one hand of type \'Both\' or one \'Dominant\' and \'Non-Dominant\' hand for the same body.");
            }

            handPrefabs.Add(handPrefab);
        }

        /// <summary>
        /// Returns the instantiated hand with the chosen dominance.
        /// </summary>
        /// <param name="dominant">Whether to get the dominant or non-dominant hand.</param>
        /// <returns></returns>
        public static HandController GetHandByDominance(bool dominant)
        {
            if (!HandsReady)
            {
                VRMod.StaticLogger.LogError("Cannot access the VR hands as they are not instantiated. Returning null.");
                return null;
            }

            return dominant ? dominantHand : nonDominantHand;
        }

        /// <summary>
        /// Returns the instantiated hand with the chosen side.
        /// </summary>
        /// <param name="dominant">Whether to get the left or right hand.</param>
        /// <returns></returns>
        public static HandController GetHandBySide(bool left)
        {
            return left == ModConfig.LeftDominantHand.Value ? dominantHand : nonDominantHand;
        }

        private static bool CheckPrefabMatch(Hand hand, GameObject prefab)
        {
            Hand prefabHand = prefab.GetComponent<Hand>();

            return hand.bodyName == prefabHand.bodyName && (prefabHand.handType == HandType.Both || hand.handType == HandType.Both || prefabHand.handType == hand.handType);
        }

        private static void InitWristHUD(On.RoR2.CameraRigController.orig_Start orig, RoR2.CameraRigController self)
        {
            orig(self);

            if (!Run.instance) return;

            if (!HandsReady)
            {
                VRMod.StaticLogger.LogInfo("Setting up hands from camera");
                SetupHands();
            }

            dominantHand.smallHud.Init(self);
            nonDominantHand.smallHud.Init(self);

            foreach (HUDQueueEntry queueEntry in wristHudQueue)
            {
                GetHandBySide(queueEntry.left).smallHud.AddHUDCluster(queueEntry.transform);
            }

            wristHudQueue.Clear();

            dominantHand.watchHud.Init(self);
            nonDominantHand.watchHud.Init(self);

            foreach (HUDQueueEntry queueEntry in watchHudQueue)
            {
                GetHandBySide(queueEntry.left).watchHud.AddHUDCluster(queueEntry.transform);
            }

            watchHudQueue.Clear();
        }

        private static void SetupHands()
        {
            HandController leftHand = GameObject.Instantiate(handSelectorPrefab).GetComponent<HandController>();
            leftHand.xrNode = XRNode.LeftHand;
            Vector3 mirroredScale = leftHand.transform.localScale;
            mirroredScale.x = -mirroredScale.x;
            leftHand.transform.localScale = mirroredScale;

            HandController rightHand = GameObject.Instantiate(handSelectorPrefab).GetComponent<HandController>();
            rightHand.xrNode = XRNode.RightHand;

            dominantHand = ModConfig.LeftDominantHand.Value ? leftHand : rightHand;
            nonDominantHand = ModConfig.LeftDominantHand.Value ? rightHand : leftHand;

            dominantHand.SetPrefabs(handPrefabs.Where((x) => CheckDominance(x, true)).ToList());
            nonDominantHand.SetPrefabs(handPrefabs.Where((x) => CheckDominance(x, false)).ToList());

            dominantHand.oppositeHand = nonDominantHand;
            nonDominantHand.oppositeHand = dominantHand;

            //RoR2Application.onFixedUpdate += CheckForLocalBody;
        }

        private static void CheckForLocalBody()
        {
            CharacterBody localBody = LocalUserManager.GetFirstLocalUser().cachedBody;
            if (localBody)
            {
                RoR2Application.onFixedUpdate -= CheckForLocalBody;
                VRMod.StaticLogger.LogInfo(String.Format("Local cached body \'{0}\' found. Applying hand pair.", localBody.name));
                SetHandPair(localBody);
            }
        }

        private static bool CheckDominance(GameObject prefab, bool dominant)
        {
            Hand hand = prefab.GetComponent<Hand>();

            return hand.handType == HandType.Both || (dominant == (hand.handType == HandType.Dominant));
        }

        internal static void SetHandPair(CharacterBody body)
        {
            if (!HandsReady)
            {
                VRMod.StaticLogger.LogInfo("Setting up hands from hand pair");
                SetupHands();
            }

            string bodyName = body.name.Substring(0, body.name.IndexOf("(Clone)"));

            dominantHand.SetCurrentHand(bodyName);
            nonDominantHand.SetCurrentHand(bodyName);

            body.aimOriginTransform = dominantHand.currentHand.currentMuzzle.transform;

            ChildLocator childLocator = body.modelLocator.modelTransform.GetComponent<ChildLocator>();

            if (childLocator)
            {
                List<ChildLocator.NameTransformPair> transformPairList = childLocator.transformPairs.ToList();

                if (!transformPairList.Exists((x) => x.name == "CurrentDominantMuzzle"))
                {
                    transformPairList.Add(new ChildLocator.NameTransformPair() { name = "CurrentDominantMuzzle", transform = dominantHand.currentHand.currentMuzzle.transform });
                    transformPairList.Add(new ChildLocator.NameTransformPair() { name = "CurrentNonDominantMuzzle", transform = nonDominantHand.currentHand.currentMuzzle.transform });
                    childLocator.transformPairs = transformPairList.ToArray();
                }
                else
                {
                    int index1 = transformPairList.FindIndex(x => x.name == "CurrentDominantMuzzle");
                    int index2 = transformPairList.FindIndex(x => x.name == "CurrentNonDominantMuzzle");

                    childLocator.transformPairs[index1].transform = dominantHand.currentHand.currentMuzzle.transform;
                    childLocator.transformPairs[index2].transform = nonDominantHand.currentHand.currentMuzzle.transform;
                }

                foreach (Muzzle muzzle in dominantHand.currentHand.muzzles)
                {
                    for (int i = 0; i < childLocator.transformPairs.Length; i++)
                    {
                        if (muzzle.entriesToReplaceIfDominant.Contains(childLocator.transformPairs[i].name))
                        {
                            childLocator.transformPairs[i].transform = muzzle.transform;
                        }
                    }

                    transformPairList = childLocator.transformPairs.ToList();

                    string pairName = muzzle.transform.name + "_Dominant";

                    if (!transformPairList.Exists((x) => x.name == pairName))
                    {
                        transformPairList.Add(new ChildLocator.NameTransformPair() { name = pairName, transform = muzzle.transform });

                        childLocator.transformPairs = transformPairList.ToArray();
                    }
                }

                foreach (Muzzle muzzle in nonDominantHand.currentHand.muzzles)
                {
                    for (int i = 0; i < childLocator.transformPairs.Length; i++)
                    {
                        if (muzzle.entriesToReplaceIfNonDominant.Contains(childLocator.transformPairs[i].name))
                        {
                            childLocator.transformPairs[i].transform = muzzle.transform;
                        }
                    }

                    transformPairList = childLocator.transformPairs.ToList();

                    string pairName = muzzle.transform.name + "_NonDominant";

                    if (!transformPairList.Exists((x) => x.name == pairName))
                    {
                        transformPairList.Add(new ChildLocator.NameTransformPair() { name = pairName, transform = muzzle.transform });

                        childLocator.transformPairs = transformPairList.ToArray();
                    }
                }
            }

            if (onHandPairSet != null)
                onHandPairSet(body);
        }

        internal static void ResetToPointer()
        {
            dominantHand.ResetToPointer();
            nonDominantHand.ResetToPointer();
        }
    }
}
