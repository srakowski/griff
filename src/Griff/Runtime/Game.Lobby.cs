using System.Text;
using Griff.Input;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Menus for network games: boxes to type a name or an address into, and the list of who's playing.
public sealed partial class Game
{
    private void RunLobby()
    {
        RunTextFields();
        RunScoreboards();
        RunNameTags();
    }

    // Every machine works out the same names for itself, so they show at once wherever the character is.
    private void RunNameTags()
    {
        foreach (var tag in Showing<NameTag>())
        {
            if (tag.Entity!.GetComponent<Text>() is not { } text)
                continue;
            var player = Net.IsOnline ? Net.OwnerOf(tag.Entity) : null;
            var shown = player is null ? Net.IsOnline ? "" : tag.Offline ?? "" : tag.ShowScore ? $"{player.Name}  {player.Score}" : player.Name;
            if (text.Value != shown)
                text.Value = shown;
            if (tag.UseColor && player is not null && text.Color != player.Color with { A = text.Color.A })
                text.Color = player.Color with { A = text.Color.A };
        }
    }

    private void RunTextFields()
    {
        var fields = Active<TextField>();
        if (fields.Count == 0)
            return;

        // A click picks the field under the mouse, or none.
        if (Input.WasPressed(MouseButton.Left) && Input.IsMouseInView)
        {
            var clicked = fields.FirstOrDefault(f => AreaOf(f.Entity!, f.Size).Contains(Input.MouseWorld));
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
                    TextFieldUse.PlayerName => string.IsNullOrEmpty(field.Value) ? Net.PlayerName : field.Value,
                    TextFieldUse.JoinAddress => SaveData.GetString(NetSession.AddressSaveKey) ?? field.Value,
                    _ => field.Value,
                };
            }

            if (field.IsFocused)
            {
                var typed = Input.TypeInto(field.Value ?? "", Math.Max(1, field.MaxLength));
                if (typed != (field.Value ?? ""))
                {
                    field.Value = typed;
                    Remember(field);
                }

                if (Input.WasPressed(Key.Enter) || Input.WasPressed(Key.Escape) || Input.WasPressed(Key.Tab))
                    field.IsFocused = false;
                field.Blink = (field.Blink + Time.UnscaledDelta) % 1;
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
                Net.PlayerName = field.Value ?? "";
                break;
            case TextFieldUse.JoinAddress when string.IsNullOrWhiteSpace(field.Value):
                SaveData.Delete(NetSession.AddressSaveKey);
                break;
            case TextFieldUse.JoinAddress:
                SaveData.Set(NetSession.AddressSaveKey, field.Value!.Trim());
                break;
        }
    }

    private void RunScoreboards()
    {
        foreach (var board in Active<Scoreboard>())
        {
            if (board.Entity!.GetComponent<Text>() is not { } text)
                continue;
            var shown = board.HoldKey != Key.None && !Input.IsDown(board.HoldKey) ? "" : Describe(board);
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

        if (!Net.IsOnline)
        {
            if (!string.IsNullOrWhiteSpace(board.Offline))
                lines.AppendLine(board.Offline.Trim());
        }
        else
        {
            var players = board.ShowScore ? Net.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Id) : Net.Players.OrderBy(p => p.Id);
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

        if (board.ShowStatus && !string.IsNullOrWhiteSpace(Net.Status))
            lines.AppendLine(Net.Status);
        return lines.ToString().TrimEnd();
    }
}
