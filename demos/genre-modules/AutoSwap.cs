// Plays a match-3 board by itself: every second it finds a swap that lines gems up and makes it. MatchBoard comes from
// the Match-3 module, and a script names it without a using, as it does a Sprite.
public class AutoSwap : Behavior
{
    private float _wait = 1;

    protected override void Update()
    {
        _wait -= Time.Delta;
        if (_wait > 0 || Game.Find<MatchBoard>() is not { IsBusy: false } board || board.FindMove() is not { } move)
            return;
        _wait = 1;
        board.Swap(move.From.X, move.From.Y, move.To.X, move.To.Y);
    }
}
