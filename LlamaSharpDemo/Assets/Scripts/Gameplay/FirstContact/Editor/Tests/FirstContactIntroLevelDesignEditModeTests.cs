using System;
using NUnit.Framework;
using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace DoodleDiplomacy.Gameplay.FirstContact.Editor.Tests
{
    public sealed class FirstContactIntroLevelDesignEditModeTests
    {
        [Test]
        public void NarrativeZone_RequiresEveryConfiguredActorInsideAuthoredBox()
        {
            GameObject zoneObject = new("Narrative Zone");
            GameObject player = new("Player");
            GameObject director = new("Director");
            try
            {
                zoneObject.transform.SetPositionAndRotation(
                    new Vector3(5f, 1f, -3f),
                    Quaternion.Euler(0f, 37f, 0f));
                BoxCollider box = zoneObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = Vector3.zero;
                box.size = new Vector3(4f, 3f, 2f);
                FirstContactIntroNarrativeZone zone =
                    zoneObject.AddComponent<FirstContactIntroNarrativeZone>();
                zone.Configure(
                    "Test zone",
                    FirstContactIntroNarrativeStage.CitizenEncounter,
                    20,
                    FirstContactIntroZoneActors.PlayerAndDirector,
                    "test.dialogue",
                    string.Empty,
                    null,
                    Color.yellow);

                player.transform.position = zoneObject.transform.position;
                director.transform.position = zoneObject.transform.position +
                                              Vector3.forward * 10f;
                zone.BindActors(player.transform, director.transform);
                zone.Arm(resetTriggered: true);
                zone.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                Assert.That(zone.HasTriggered, Is.False);

                director.transform.position = zoneObject.transform.position;
                zone.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                Assert.That(zone.HasTriggered, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(zoneObject);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(director);
            }
        }

        [Test]
        public void NarrativeZone_CanRememberActorsEnteringAtDifferentTimes()
        {
            GameObject zoneObject = new("Private Exchange Zone");
            GameObject player = new("Player");
            GameObject director = new("Director");
            try
            {
                BoxCollider box = zoneObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(3f, 3f, 3f);
                FirstContactIntroNarrativeZone zone =
                    zoneObject.AddComponent<FirstContactIntroNarrativeZone>();
                zone.Configure(
                    "Private exchange",
                    FirstContactIntroNarrativeStage.PrivateExchange,
                    30,
                    FirstContactIntroZoneActors.PlayerAndDirector,
                    "test.private_exchange",
                    string.Empty,
                    null,
                    Color.yellow);

                player.transform.position = zoneObject.transform.position;
                director.transform.position = Vector3.forward * 10f;
                zone.BindActors(player.transform, director.transform);
                zone.Arm(
                    resetTriggered: true,
                    rememberActorEntries: true);
                zone.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                Assert.That(zone.HasTriggered, Is.False);

                player.transform.position = Vector3.back * 10f;
                director.transform.position = zoneObject.transform.position;
                zone.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                Assert.That(zone.HasTriggered, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(zoneObject);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(director);
            }
        }

        [Test]
        public void Guide_PausesAtNamedAuthoredHoldPoint()
        {
            GameObject guideObject = new("Guide");
            GameObject player = new("Player");
            GameObject targetObject = new("Named Hold");
            try
            {
                FirstContactIntroGuideController guide =
                    guideObject.AddComponent<FirstContactIntroGuideController>();
                FirstContactIntroGuidePoint point =
                    targetObject.AddComponent<FirstContactIntroGuidePoint>();
                point.Configure("Citizen conversation", 0, pause: true);
                guide.Configure(
                    new[] { targetObject.transform },
                    Array.Empty<int>(),
                    speed: 2.25f,
                    leashDistance: 5.5f);

                FirstContactIntroGuidePoint reachedPoint = null;
                guide.ReachedNamedHoldPoint += reached => reachedPoint = reached;
                guide.Begin(player.transform, warpToStart: true);
                guide.SendMessage("Update", SendMessageOptions.DontRequireReceiver);

                Assert.That(guide.IsWaitingForRelease, Is.True);
                Assert.That(reachedPoint, Is.SameAs(point));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(guideObject);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void Guide_CanUseNonPausePointAsSequenceCatchUpGate()
        {
            GameObject guideObject = new("Guide");
            GameObject player = new("Player");
            GameObject catchUpObject = new("Catch-up Gate");
            try
            {
                FirstContactIntroGuideController guide =
                    guideObject.AddComponent<FirstContactIntroGuideController>();
                FirstContactIntroGuidePoint point =
                    catchUpObject.AddComponent<FirstContactIntroGuidePoint>();
                point.Configure("Private exchange", 0, pause: false);
                guide.Configure(
                    new[] { catchUpObject.transform },
                    Array.Empty<int>(),
                    speed: 2.25f,
                    leashDistance: 5.5f);
                guide.AddSequenceHoldPoint(point);

                guide.Begin(player.transform, warpToStart: true);
                guide.SendMessage("Update", SendMessageOptions.DontRequireReceiver);

                Assert.That(point.PauseOnArrival, Is.False);
                Assert.That(guide.IsWaitingForRelease, Is.True);
                Assert.That(guide.IsWaitingAt(point), Is.True);
                guide.Resume();
                Assert.That(guide.IsWaitingForRelease, Is.False);
                Assert.That(guide.IsWaitingAt(point), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(guideObject);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(catchUpObject);
            }
        }

        [Test]
        public void RouteEditor_InsertsPointBetweenNeighborsAndRenumbersPath()
        {
            GameObject root = new("Route Root");
            GameObject guideObject = new("Guide");
            GameObject firstObject = new("Route 00");
            GameObject lastObject = new("Route 01");
            FirstContactIntroGuidePoint inserted = null;
            try
            {
                guideObject.transform.SetParent(root.transform);
                firstObject.transform.SetParent(root.transform);
                lastObject.transform.SetParent(root.transform);
                firstObject.transform.position = Vector3.zero;
                lastObject.transform.position = Vector3.forward * 4f;

                FirstContactIntroGuideController guide =
                    guideObject.AddComponent<FirstContactIntroGuideController>();
                FirstContactIntroGuidePoint first =
                    firstObject.AddComponent<FirstContactIntroGuidePoint>();
                FirstContactIntroGuidePoint last =
                    lastObject.AddComponent<FirstContactIntroGuidePoint>();
                first.Configure("First", 0, pause: false);
                last.Configure("Last", 1, pause: false);
                guide.Configure(
                    new[] { firstObject.transform, lastObject.transform },
                    Array.Empty<int>(),
                    speed: 2.25f,
                    leashDistance: 5.5f);

                inserted = FirstContactIntroRouteEditing.InsertRelative(
                    first,
                    insertBefore: false);

                Assert.That(inserted, Is.Not.Null);
                Assert.That(guide.PathPoints.Count, Is.EqualTo(3));
                Assert.That(guide.PathPoints[0], Is.SameAs(firstObject.transform));
                Assert.That(guide.PathPoints[1], Is.SameAs(inserted.transform));
                Assert.That(guide.PathPoints[2], Is.SameAs(lastObject.transform));
                Assert.That(inserted.transform.position,
                    Is.EqualTo(Vector3.forward * 2f));
                Assert.That(first.RouteOrder, Is.EqualTo(0));
                Assert.That(inserted.RouteOrder, Is.EqualTo(1));
                Assert.That(last.RouteOrder, Is.EqualTo(2));
            }
            finally
            {
                if (inserted != null)
                {
                    UnityEngine.Object.DestroyImmediate(inserted.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void VehicleDebugSkip_SnapsAuthoredRigToParkingAnchor()
        {
            GameObject routeEnvironment = new("Route Environment");
            GameObject vehicle = new("Vehicle Motion Root");
            GameObject player = new("Player");
            GameObject cruiseStart = new("Cruise Start");
            GameObject turnEntry = new("Turn Entry");
            GameObject turnExit = new("Turn Exit");
            GameObject parkingStop = new("Parking Stop");
            try
            {
                routeEnvironment.transform.SetPositionAndRotation(
                    new Vector3(10f, 0f, -4f),
                    Quaternion.Euler(0f, 35f, 0f));
                vehicle.transform.SetParent(routeEnvironment.transform, false);
                cruiseStart.transform.SetParent(routeEnvironment.transform, false);
                turnEntry.transform.SetParent(routeEnvironment.transform, false);
                turnExit.transform.SetParent(routeEnvironment.transform, false);
                parkingStop.transform.SetParent(routeEnvironment.transform, false);
                turnEntry.transform.localPosition = new Vector3(0f, 0f, 3f);
                turnExit.transform.localPosition = new Vector3(2f, 0f, 5f);
                parkingStop.transform.localPosition = new Vector3(2f, 0f, 9f);

                FirstContactVehicleRouteController route =
                    routeEnvironment.AddComponent<FirstContactVehicleRouteController>();
                route.Configure(player.transform);
                route.ConfigureSceneRoute(
                    routeEnvironment.transform,
                    vehicle.transform,
                    cruiseStart.transform,
                    turnEntry.transform,
                    turnExit.transform,
                    parkingStop.transform,
                    null);

                bool snapped = route.SnapToParkedPose();

                Assert.That(snapped, Is.True);
                Assert.That(route.State, Is.EqualTo(FirstContactVehicleDriveState.Stopped));
                Assert.That(
                    Vector3.Distance(vehicle.transform.position, parkingStop.transform.position),
                    Is.LessThan(0.001f));
                Assert.That(
                    Quaternion.Angle(
                        vehicle.transform.rotation,
                        routeEnvironment.transform.rotation),
                    Is.LessThan(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(routeEnvironment);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void PlayerTeleport_RestoresAuthoredCameraPoseWhenStanding()
        {
            GameObject playerObject = new("Player");
            GameObject cameraObject = new("Authored Camera");
            GameObject targetObject = new("Teleport Target");
            try
            {
                cameraObject.transform.SetParent(playerObject.transform, false);
                Vector3 authoredPosition = new(0.14f, 1.37f, -0.21f);
                Quaternion authoredRotation = Quaternion.Euler(7f, 0f, 0f);
                cameraObject.transform.SetLocalPositionAndRotation(
                    authoredPosition,
                    authoredRotation);
                UnityCamera camera = cameraObject.AddComponent<UnityCamera>();
                FirstContactIntroPlayerController player =
                    playerObject.AddComponent<FirstContactIntroPlayerController>();
                player.Configure(camera, null);

                cameraObject.transform.SetLocalPositionAndRotation(
                    new Vector3(0f, 4f, 0f),
                    Quaternion.identity);
                targetObject.transform.SetPositionAndRotation(
                    new Vector3(8f, 0f, 3f),
                    Quaternion.Euler(0f, 90f, 0f));

                player.Teleport(targetObject.transform, seated: false);

                Assert.That(
                    Vector3.Distance(
                        cameraObject.transform.localPosition,
                        authoredPosition),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(
                        cameraObject.transform.localRotation,
                        authoredRotation),
                    Is.LessThan(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PlayerRepositionPreservingView_KeepsCurrentCameraPose()
        {
            GameObject playerObject = new("Player");
            GameObject cameraObject = new("Camera");
            try
            {
                cameraObject.transform.SetParent(playerObject.transform, false);
                UnityCamera camera = cameraObject.AddComponent<UnityCamera>();
                FirstContactIntroPlayerController player =
                    playerObject.AddComponent<FirstContactIntroPlayerController>();
                player.Configure(camera, null);

                Vector3 currentCameraPosition = new(0.08f, 1.61f, -0.12f);
                Quaternion currentCameraRotation = Quaternion.Euler(-31f, 0f, 0f);
                cameraObject.transform.SetLocalPositionAndRotation(
                    currentCameraPosition,
                    currentCameraRotation);
                Vector3 targetPosition = new(12f, -3f, 7f);
                Quaternion targetRotation = Quaternion.Euler(0f, 143f, 0f);

                player.RepositionPreservingView(targetPosition, targetRotation);

                Assert.That(
                    Vector3.Distance(playerObject.transform.position, targetPosition),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(playerObject.transform.rotation, targetRotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Distance(
                        cameraObject.transform.localPosition,
                        currentCameraPosition),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(
                        cameraObject.transform.localRotation,
                        currentCameraRotation),
                    Is.LessThan(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }
    }
}
