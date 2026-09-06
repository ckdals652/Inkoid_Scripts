public class PlayerStatus
{
    public float Health;
    public float MaximumHealth;

    public float Ink;
    public float MaximumInk;

    public float HorizontalityMoveSpeed;
    public float VerticalMoveSpeed;
    public float MaximumMoveSpeed;

    public float DashForce;

    // 돌진 무기에 대미지 감소율이 필요하여 스탯 추가 (9/17 유빈 추가)
    public float DamegeReduction;


    public PlayerStatus(
        float health, float maximumHealth,
        float ink, float maximumInk,
        float horizontalityMoveSpeed, float verticalMoveSpeed, float maximumMoveSpeed,
        float dashForce,
        float damegeReduction
    )
    {
        Health = health;
        MaximumHealth = maximumHealth;

        Ink = ink;
        MaximumInk = maximumInk;

        HorizontalityMoveSpeed = horizontalityMoveSpeed;
        VerticalMoveSpeed = verticalMoveSpeed;
        MaximumMoveSpeed = maximumMoveSpeed;

        DashForce = dashForce;
        DamegeReduction = damegeReduction;
    }
}