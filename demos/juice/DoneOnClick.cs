// Takes a finished job off the list when it's clicked: its Layout slides the rest up into the gap.
public class DoneOnClick : Behavior
{
    protected override void OnClick() => Entity!.IsEnabled = false;
}
