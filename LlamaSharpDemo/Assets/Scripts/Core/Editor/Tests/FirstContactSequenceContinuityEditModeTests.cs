using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class FirstContactSequenceContinuityEditModeTests
    {
        [Test]
        public void IntroExitCommitsMeetingEquipmentOnlyForCompletedHandoff()
        {
            var root = new GameObject("FacilitySequenceContinuityTest");
            var equipment = new GameObject("Equipment").transform;
            var briefingPose = new GameObject("BriefingPose").transform;
            var meetingPose = new GameObject("MeetingPose").transform;
            var carrySocket = new GameObject("CarrySocket").transform;

            try
            {
                briefingPose.SetPositionAndRotation(
                    new Vector3(1f, 2f, 3f),
                    Quaternion.Euler(0f, 10f, 0f));
                meetingPose.SetPositionAndRotation(
                    new Vector3(11f, 12f, 13f),
                    Quaternion.Euler(0f, 80f, 0f));
                carrySocket.SetPositionAndRotation(
                    new Vector3(21f, 22f, 23f),
                    Quaternion.Euler(0f, 140f, 0f));

                FirstContactIntroSequenceController sequence =
                    root.AddComponent<FirstContactIntroSequenceController>();
                ConfigureSequence(
                    sequence,
                    equipment,
                    briefingPose,
                    meetingPose,
                    carrySocket);

                FirstContactIntroMode mode = root.AddComponent<FirstContactIntroMode>();
                mode.Configure(
                    "facility-test",
                    references: null,
                    sequence: sequence);
                GameplayModeHost host = root.AddComponent<GameplayModeHost>();
                GameplayModeContext context = CreateEmptyContext();
                Assert.IsTrue(host.EnterMode(mode, context));

                equipment.SetPositionAndRotation(
                    carrySocket.position,
                    carrySocket.rotation);
                host.ExitActiveMode(GameplayModeExitReason.Completed);

                AssertPoseMatches(equipment, meetingPose);

                Assert.IsTrue(host.EnterMode(mode, context));
                equipment.SetPositionAndRotation(
                    carrySocket.position,
                    carrySocket.rotation);
                host.ExitActiveMode(GameplayModeExitReason.Cancelled);

                AssertPoseMatches(equipment, briefingPose);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(equipment.gameObject);
                Object.DestroyImmediate(briefingPose.gameObject);
                Object.DestroyImmediate(meetingPose.gameObject);
                Object.DestroyImmediate(carrySocket.gameObject);
            }
        }

        [Test]
        public void EquipmentContinuityRestoresInteractionStateAfterCarry()
        {
            var equipment = new GameObject("Equipment").transform;
            var briefingPose = new GameObject("BriefingPose").transform;
            var meetingPose = new GameObject("MeetingPose").transform;
            var carrySocket = new GameObject("CarrySocket").transform;

            try
            {
                BoxCollider collider = equipment.gameObject.AddComponent<BoxCollider>();
                InteractableObject interactable =
                    equipment.gameObject.AddComponent<InteractableObject>();
                briefingPose.position = new Vector3(1f, 0f, 0f);
                meetingPose.position = new Vector3(2f, 0f, 0f);
                carrySocket.position = new Vector3(3f, 0f, 0f);

                var continuity = new FirstContactEquipmentContinuity(
                    new[] { equipment },
                    new[] { briefingPose },
                    new[] { meetingPose },
                    new[] { carrySocket });

                Assert.IsTrue(continuity.AttachToCarriersImmediate());
                AssertPoseMatches(equipment, carrySocket);
                Assert.IsFalse(collider.enabled);
                Assert.IsFalse(interactable.isActive);

                continuity.CommitMeetingPlacement();
                AssertPoseMatches(equipment, meetingPose);
                Assert.IsTrue(collider.enabled);
                Assert.IsTrue(interactable.isActive);

                interactable.SetInteractable(false);
                Assert.IsTrue(continuity.AttachToCarriersImmediate());
                continuity.ResetToBriefingPlacement();
                AssertPoseMatches(equipment, briefingPose);
                Assert.IsTrue(collider.enabled);
                Assert.IsFalse(interactable.isActive);
            }
            finally
            {
                Object.DestroyImmediate(equipment.gameObject);
                Object.DestroyImmediate(briefingPose.gameObject);
                Object.DestroyImmediate(meetingPose.gameObject);
                Object.DestroyImmediate(carrySocket.gameObject);
            }
        }

        private static void ConfigureSequence(
            FirstContactIntroSequenceController sequence,
            Transform equipment,
            Transform briefingPose,
            Transform meetingPose,
            Transform carrySocket)
        {
            var serialized = new SerializedObject(sequence);
            serialized.FindProperty("segment").enumValueIndex =
                (int)FirstContactIntroSegment.Facility;
            SetTransformArray(serialized.FindProperty("transferableEquipment"), equipment);
            SetTransformArray(serialized.FindProperty("briefingEquipmentPoses"), briefingPose);
            SetTransformArray(serialized.FindProperty("meetingEquipmentPoses"), meetingPose);
            SetTransformArray(serialized.FindProperty("equipmentCarrySockets"), carrySocket);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetTransformArray(
            SerializedProperty property,
            Transform value)
        {
            property.arraySize = 1;
            property.GetArrayElementAtIndex(0).objectReferenceValue = value;
        }

        private static GameplayModeContext CreateEmptyContext()
        {
            return new GameplayModeContext(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        private static void AssertPoseMatches(Transform actual, Transform expected)
        {
            Assert.That(
                Vector3.Distance(actual.position, expected.position),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(actual.rotation, expected.rotation),
                Is.LessThan(0.001f));
        }
    }
}
