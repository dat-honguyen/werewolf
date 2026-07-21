using Alba;
using Application.Werewolf.Domain;
using Application.Werewolf.Lobby.CreateLobby;
using Application.Werewolf.Lobby.GetLobby;
using Application.Werewolf.Lobby.JoinLobby;
using Shouldly;
using System.Net;

namespace IntegrationTests;

[Collection("scenarios")]
public class LobbyFlowTests(AppFixture appFixture, ITestOutputHelper outputHelper)
    : IntegrationTest(appFixture, outputHelper)
{
    [Fact]
    public async Task Creating_a_lobby_and_joining_it_shows_both_players()
    {
        var hostId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();

        var (_, createResult) = await TrackedHttpCall(x =>
        {
            x.Post.Json(new CreateLobby { HostPlayerId = hostId, HostDisplayName = "Alice" }).ToUrl("/api/v1/lobby");
            x.StatusCodeShouldBeOk();
        });

        var created = await createResult!.ReadAsJsonAsync<CreateLobbyResponse>();
        created.ShouldNotBeNull();
        created.RoomCode.ShouldNotBeNullOrWhiteSpace();

        await TrackedHttpCall(x =>
        {
            x.Post.Json(new JoinLobby { RoomCode = new(created.RoomCode), PlayerId = joinerId, DisplayName = "Bob" })
                .ToUrl("/api/v1/lobby/join");
            x.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        var (_, getResult) = await TrackedHttpCall(x =>
        {
            x.Get.Url($"/api/v1/lobby/{created.RoomCode}");
            x.StatusCodeShouldBeOk();
        });

        var lobby = await getResult!.ReadAsJsonAsync<LobbyStateResponse>();
        lobby.ShouldNotBeNull();
        lobby.HostPlayerId.ShouldBe(hostId);
        lobby.Players.Count.ShouldBe(2);
        lobby.Players.ShouldContain(p => p.PlayerId == joinerId && p.DisplayName == "Bob");
    }
}
