
public static class InkEvents
{
    public static event System.Action<int, float> OnInkConsumed;
    public static event System.Action<int, float> OnInkRestored;
    public static event System.Action<int> OnBoostInkRecovery;
    public static event System.Action<int> OnResetInkRecovery;
    public static event System.Action<int> OnFullInk;
    public static event System.Action<int, float> OnItemInkBoostRecovery;

    public static void ConsumeInk(int viewId, float amount) => OnInkConsumed?.Invoke(viewId, amount);
    public static void RestoreInk(int viewId, float amount) => OnInkRestored?.Invoke(viewId, amount);
    public static void BoostInkRecovery(int viewId) => OnBoostInkRecovery?.Invoke(viewId);
    public static void ResetInkRecovery(int viewId) => OnResetInkRecovery?.Invoke(viewId);
    public static void FullInk(int viewId) => OnFullInk?.Invoke(viewId);
    public static void ItemInkBoostRecovery(int viewId, float amt) => OnItemInkBoostRecovery?.Invoke(viewId, amt);


}