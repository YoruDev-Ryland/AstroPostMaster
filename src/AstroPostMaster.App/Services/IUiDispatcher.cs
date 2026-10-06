namespace AstroPostMaster.App.Services;

/// <summary>Marshals work onto the UI thread (immediate in tests).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
