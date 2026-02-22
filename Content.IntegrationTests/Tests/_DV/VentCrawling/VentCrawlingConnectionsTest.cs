using System.Numerics;
using Content.Shared._DV.VentCrawling.Components;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._DV.VentCrawling;

[TestFixture]
public sealed class VentCrawlingConnectionsTest
{
    [Test]
    public async Task AdjacentVentsAutoConnect()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        EntityUid ventA = default;
        EntityUid ventB = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var mapMan = server.ResolveDependency<IMapManager>();
            var mapSys = server.System<SharedMapSystem>();
            var xform = server.System<SharedTransformSystem>();

            mapSys.CreateMap(out var mapId);
            var grid = mapMan.CreateGrid(mapId);

            ventA = entMan.SpawnAtPosition(null, new EntityCoordinates(grid, Vector2.Zero));
            ventB = entMan.SpawnAtPosition(null, new EntityCoordinates(grid, new Vector2(1, 0)));

            entMan.EnsureComponent<VentCrawlableComponent>(ventA);
            entMan.EnsureComponent<VentCrawlableComponent>(ventB);

            xform.SetAnchored(ventA, true);
            xform.SetAnchored(ventB, true);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var compA = entMan.GetComponent<VentCrawlableComponent>(ventA);
            var compB = entMan.GetComponent<VentCrawlableComponent>(ventB);

            Assert.That((compA.Connections & Direction.East) != 0, Is.True);
            Assert.That((compB.Connections & Direction.West) != 0, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DisconnectedVentsDoNotConnect()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        EntityUid ventA = default;
        EntityUid ventB = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var mapMan = server.ResolveDependency<IMapManager>();
            var mapSys = server.System<SharedMapSystem>();
            var xform = server.System<SharedTransformSystem>();

            mapSys.CreateMap(out var mapId);
            var grid = mapMan.CreateGrid(mapId);

            ventA = entMan.SpawnAtPosition(null, new EntityCoordinates(grid, Vector2.Zero));
            ventB = entMan.SpawnAtPosition(null, new EntityCoordinates(grid, new Vector2(2, 0)));

            entMan.EnsureComponent<VentCrawlableComponent>(ventA);
            entMan.EnsureComponent<VentCrawlableComponent>(ventB);

            xform.SetAnchored(ventA, true);
            xform.SetAnchored(ventB, true);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var compA = entMan.GetComponent<VentCrawlableComponent>(ventA);
            var compB = entMan.GetComponent<VentCrawlableComponent>(ventB);

            Assert.That(compA.Connections, Is.EqualTo(Direction.Invalid));
            Assert.That(compB.Connections, Is.EqualTo(Direction.Invalid));
        });

        await pair.CleanReturnAsync();
    }
}
