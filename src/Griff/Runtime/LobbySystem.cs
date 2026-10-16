using System.Text;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// A network game's lobby with no code: name tags, text fields players type in, and scoreboards.
/// </summary>
internal sealed class LobbySystem : GameSystem
{
    public override string Name => "Lobby";

    internal void RunLobby()
    {
        RunTextFields();
        RunScoreboards();
        RunNameTags();
    }

    // Every machine works out the same names for itself, so they show at once wherever the character is.
    private void RunNameTags()
    {
        foreach (var tag in Game.Showing<NameTag>())
        {
            if (tag.Entity!.GetComponent<Text>() is not { } text)
                continue;
            var player = Game.Net.IsOnline ? Game.Net.OwnerOf(tag.Entity) : null;
            var shown = player is null ? Game.Net.IsOnline ? "" : tag.Offline ?? "" : tag.ShowScore ? $"{player.Name}  {player.Score}" : player.Name;
            if (text.Value != shown)
                text.Value = shown;
            if (tag.UseColor && player is not null && text.Color != player.Color with { A = text.Color.A })
                text.Color = player.Color with { A = text.Color.A };
        }
    }

    private void RunTextFields()
    {
        var fields = Game.Active<TextField>();
        if (fields.Count == 0)
            return;

        // A click picks the field under the mouse, or none.
        if (Game.Input.WasPressed(MouseButton.Left) && Game.Input.IsMouseInView)
        {
            var clicked = fields.FirstOrDefault(f => MenuSystem.AreaOf(f.Entity!, f.Size).Contains(Game.Input.MouseWorld));
            foreach (var field in fields)
                field.IsFocused = field == clicked;
        }

        foreach (var field in fields)
        {
            if (!field.Loaded)
            {
                field.Loaded = true;
                field.Value = field.Use switch
                {
                    TextFieldUse.PlayerName => string.IsNullOrEmpty(field.Value) ? Game.Net.PlayerName : field.Value,
                    TextFieldUse.JoinAddress => Game.SaveData.GetString(NetSession.AddressSaveKey) ?? field.Value,
                    _ => field.Value,
                };
            }

            if (field.IsFocused)
            {
                var typed = Game.Input.TypeInto(field.Value ?? "", Math.Max(1, field.MaxLength));
                if (typed != (field.Value ?? ""))
                {
                    field.Value = typed;
                    Remember(field);
                }

                if (Game.Input.WasPressed(Key.Enter) || Game.Input.WasPressed(Key.Escape) || Game.Input.WasPressed(Key.Tab))
                    field.IsFocused = false;
                field.Blink = (field.Blink + Game.Time.UnscaledDelta) % 1;
            }

            if (field.Entity!.GetComponent<Text>() is { } text)
            {
                var shown = string.IsNullOrEmpty(field.Value) && !field.IsFocused ? field.Placeholder ?? "" : field.Value ?? "";
                if (field.IsFocused && field.Blink < 0.5f)
                    shown += "|";
                if (text.Value != shown)
                    text.Value = shown;
            }
        }
    }

    private void Remember(TextField field)
    {
        switch (field.Use)
        {
            case TextFieldUse.PlayerName:
                Game.Net.PlayerName = field.Value ?? "";
                break;
            case TextFieldUse.JoinAddress when string.IsNullOrWhiteSpace(field.Value):
                Game.SaveData.Delete(NetSession.AddressSaveKey);
                break;
            case TextFieldUse.JoinAddress:
                Game.SaveData.Set(NetSession.AddressSaveKey, field.Value!.Trim());
                break;
        }
    }

    private void RunScoreboards()
    {
        foreach (var board in Game.Active<Scoreboard>())
        {
            if (board.Entity!.GetComponent<Text>() is not { } text)
                continue;
            var shown = board.HoldKey != Key.None && !Game.Input.IsDown(board.HoldKey) ? "" : Describe(board);
            if (text.Value != shown)
                text.Value = shown;

            // Text grows both ways from its middle; pinned to the top or bottom of the screen, the list grows away from it.
            if (board.Entity.GetComponent<ScreenAnchor>() is { } anchor)
            {
                var offset = board.AnchorOffset ??= anchor.Offset;
                var grow = shown.Count(c => c == '\n') * text.Size / 2;
                var way = anchor.Anchor switch
                {
                    Anchor.TopLeft or Anchor.Top or Anchor.TopRight => -1,
                    Anchor.BottomLeft or Anchor.Bottom or Anchor.BottomRight => 1,
                    _ => 0,
                };
                anchor.Offset = offset + new System.Numerics.Vector2(0, way * grow);
            }
        }
    }

    private string Describe(Scoreboard board)
    {
        var lines = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(board.Title))
            lines.AppendLine(board.Title.Trim());

        if (!Game.Net.IsOnline)
        {
            if (!string.IsNullOrWhiteSpace(board.Offline))
                lines.AppendLine(board.Offline.Trim());
        }
        else
        {
            var players = board.ShowScore ? Game.Net.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Id) : Game.Net.Players.OrderBy(p => p.Id);
            foreach (var player in players)
            {
                var line = new StringBuilder(player.Name);
                if (player.IsLocal)
                    line.Append(" (you)");
                if (player.Team.Length > 0)
                    line.Append("  ").Append(player.Team);
                if (board.ShowScore)
                    line.Append("  ").Append(player.Score);
                if (board.ShowPing && !player.IsHost)
                    line.Append("  ").Append(player.Ping).Append(" ms");
                else if (board.ShowPing)
                    line.Append("  host");
                lines.AppendLine(line.ToString());
            }
        }

        if (board.ShowStatus && !string.IsNullOrWhiteSpace(Game.Net.Status))
            lines.AppendLine(Game.Net.Status);
        return lines.ToString().TrimEnd();
    }
}
