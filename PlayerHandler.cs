using DG.Tweening;
using Photon.Pun;
using Photon.Realtime;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerHandler : MonoBehaviourPun, IPunInstantiateMagicCallback
{
    public PlayerInfo Info;
    
    public PlayerStatus Status { get; internal set; }

    public PlayerCameraController playerCameraController;

    //public Renderer characterRenderer;
    public SkinnedMeshRenderer characterRenderer;
    [SerializeField] private string characterRendererText = "Body";
    readonly string LayerName_TeamA = "TeamA", LayerName_TeamB = "TeamB";

    public PlayerAction playerAction;
    public bool isDash = false;
    public float dashInkCost = 10f;
    public bool isAttack = false;
    public bool isTryGrapple = false;
    public bool isDamaged = true;

    // 무기가 드릴일 경우 무기 시동 중 대시 분기 처리에 사용할 프로퍼티 (10/1 유빈)
    public bool isDrillUsing { get; internal set; }

    //피격 애니메이션 전용
    public event Action<float> OnDamageTaken; //  피격 애니메이션 전용 추가함

    // 데미지 받을때 상호작용용 콜백
    public Action OnDamaged;

    //아이템관련 이벤트
    //public event Action<float> OnHpUIChanged;   //  HP UI 전용 추가함
    public event Action<float> OnInkChanged;
    public event Action<float> OnHealthChanged; // 이건 이벤트 구독을 안하는데요?
    public Action<float> OnBulletSizeChanged; // 총알 크기 변경
    public Action<float> OnBrushSizeChanged; // 브러시(물감) 크기 변경
    public event Action<PlayerHandler> OnDeath;
    public event Action OnBeforeDeath;

    public PhotonView playerPhotonView;

    public Transform WeaponTransform;

    public Weapon_Base weapon;

    [SerializeField] private GameObject deathEffectPrefab;

    //public HealthBar healthBar; // 추가함


    //  injectInk FillAmount 제어용
    private Renderer _injectorRenderer;
    private int _fillAmountPropID;
    private int _teamColorPropID;
    private Color _teamColor;
    private MaterialPropertyBlock _mpb;

    private PlayerInkHandler _inkHandler;

    private int Barriers = 1 << (int)EnumLayer.LayerType.BarrierTeamA
                           | 1 << (int)EnumLayer.LayerType.BarrierTeamB;

    //대쉬 최대 속도 제어용(두트윈 써보기)
    public float originMaxSpeed { get; private set; }
    private float boostMaxSpeed = 60f;
    private float raiseDuration = 0f; // 올라가는 데 걸리는 시간
    private float holdDuration = 0.5f; // 최고점에서 유지하는 시간
    private float lowerDuration = 0.5f; // 다시 내려오는 데 걸리는 시간
    
    //기믹(블랙홀, 대쉬링 등)에 의해 강제 이동 중인지 체크하는 플래그
    public bool isGimmickActive = false;

    // 현재 실행 중인 시퀀스를 저장하기 위한 변수
    private Sequence mySequence;

    private PlayerEffectHandler _effectHandler;

    // 대시, 발사 불가 경고음 재생 시 사용
    // 해당 sfx 재생 가능 상태까지 남은 딜레이

    float warningSoundPlayDelay = 0;

    // 해당 sfx 플레이 시 딜레이 시간, 
    public readonly float warningSoundPlayDelay_Played = 1f,
        // 재생 볼륨 스케일
        volumeScale = 2;

    private EmoteFaceController _emote; // 이모트 페이스 컨트롤러 캐싱용

    public bool IsBot { get; private set; } = false;

    // GameHUDUI / StatsManager 등에서 공통으로 쓸 UI용 ID
    // 사람: ActorNumber, 봇: SequenceNumber(10000+...).
    public int UiId
    {
        get
        {
            // 봇은 Info.SequenceNumber(이미 10000+BotSeq 형태로 세팅됨)
            if (IsBot)
            {
                return Info != null ? Info.SequenceNumber : photonView.ViewID + 10000;
            }

            // 사람은 ActorNumber (PhotonNetwork.PlayerList에서 쓰는 값)
            return photonView.OwnerActorNr;
        }
    }


    private int _lastBotAttackerSeq = -1;
    private float _lastBotAttackerAt = -999f;

    private void Awake()
    {

        playerAction = new PlayerAction();
        // --- 봇 여부를 Awake 단계에서 미리 판정(OnEnable보다 먼저!) ---
        bool isBotLocal = false;
        var instData = photonView.InstantiationData;
        if (instData != null && instData.Length > 0)
        {
            if (instData[0] is int fInt) isBotLocal = (fInt == 1);
            else if (instData[0] is byte fByte) isBotLocal = (fByte == 1);
        }

        // 🟢 KeyCustomManager가 존재하면, 공유된 PlayerAction 인스턴스를 가져옵니다.
        //if (KeyCustomManager_V2.instance != null && !isBotLocal && playerPhotonView.IsMine)
        //{
        //    playerAction = KeyCustomManager_V2.instance.playerAction;
        //}
        //else
        //{
        //    // KeyCustomManager가 없는 씬(예: 테스트 씬)을 위해 예외 처리
        //    //Debug.LogError("KeyCustomManager_V2 인스턴스를 찾을 수 없습니다! 입력을 받지 못할 수 있습니다.");
        //    playerAction = new PlayerAction(); // 임시 인스턴스 생성
        //}

        //characterRenderer = transform.Find(characterRendererText).GetComponent<Renderer>();
        if (TryGetComponent(out playerPhotonView))
        {
            if (photonView.IsMine && !isBotLocal)
            {
                RPCManager.Instance.SetPlayerPhotonView(playerPhotonView);
                if (KeyCustomManager_V2.instance != null)
                    playerAction = KeyCustomManager_V2.instance.playerAction;
            }
        }
        else
        {
            playerPhotonView = GetComponent<PhotonView>();
        }

        if (characterRenderer == null)
            characterRenderer = GetComponentInChildren<SkinnedMeshRenderer>();

        playerCameraController = GetComponent<PlayerCameraController>();

        // 로컬 인간 플레이어에게만 카메라 각도 기반 숨김 컨트롤러 추가
        if (photonView.IsMine && !isBotLocal)
        {
            if (GetComponent<CameraAnglePlayerHider>() == null)
            {
                gameObject.AddComponent<CameraAnglePlayerHider>();
            }
        }

        // injectorInk 렌더러 찾기
        var inj = transform.Find("injector/injectorInk");
        if (inj != null)
            _injectorRenderer = inj.GetComponent<Renderer>();

        // 셰이더 프로퍼티 ID 캐싱
        _fillAmountPropID = Shader.PropertyToID("_FillAmount");
        _teamColorPropID = Shader.PropertyToID("_TeamColor");

        // MPB 초기화
        _mpb = new MaterialPropertyBlock();

        // 잉크 핸들러 캐싱
        _inkHandler = GetComponent<PlayerInkHandler>();
        _effectHandler = GetComponent<PlayerEffectHandler>();

        _emote = GetComponentInChildren<EmoteFaceController>(true);

        // PlayerHandler.cs의 Awake() 메서드 맨 아래에 추가
        //if (KeyCustomManager_V2.instance != null)
        //{
        //    Debug.Log("PlayerHandler가 사용하는 playerAction은 " +
        //              (playerAction == KeyCustomManager_V2.instance.playerAction
        //                  ? "KeyCustomManager의 것입니다."
        //                  : "새로 만들어진 것입니다. (문제 발생!)"));
        //}
        //else
        //{
        //    Debug.Log("KeyCustomManager를 찾지 못했습니다. (문제 발생!)");
        //}
    }

    private void Start()
    {
        if (!photonView.IsMine) return;
        PlayerColorSync();
        // if (healthBar != null)
        // {
        //     healthBar.Init(this.transform); // 반드시 Init() 호출해야 IsMine 판단 실행됨
        //
        //     if (!photonView.IsMine)
        //     {
        //         healthBar.SetHealth(Status.Health, Status.MaximumHealth);
        //     }
        // }
        UpdateInkFill(_inkHandler.Ink);

        originMaxSpeed = Status.MaximumMoveSpeed;

        // 해당 클라의 플레이어만, 방향 지시 표식에 자신을 기준으로 등록하도록
        if (!IsBot)
        {
            if (DirectionIndicator.Instance != null)
                DirectionIndicator.Instance.SetPlayer(transform);
        }
    }

    private void OnEnable()
    {
        //  --- 봇 여부 선판별: InstantiationData 이용 ---
        var instData = photonView.InstantiationData;
        if (instData != null && instData.Length > 0)
        {
            // data[0] == 1 이면 봇
            if (instData[0] is int fInt && fInt == 1) IsBot = true;
            else if (instData[0] is byte fByte && fByte == 1) IsBot = true;
        }

        // --- 최종 보정: 로컬 인간만 RPCManager 리스너로 등록 ---
        if (photonView.IsMine && !IsBot && playerPhotonView != null && RPCManager.Instance != null)
            RPCManager.Instance.SetPlayerPhotonView(playerPhotonView);

        //  --- 봇이면 입력/로컬 컨트롤 비활성 ---
        if (IsBot)
        {
            var pc = GetComponent<PlayerController>();
            if (pc) pc.enabled = false;

            var pi = GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (pi) pi.enabled = false;
        }

        _inkHandler.OnInkChanged += UpdateInkFill;

        if (GameManager.Instance != null)
        {
            // 키 값으로는 고유 ID인 ActorNumber를 사용하는 것이 좋습니다.
            //GameManager.Instance.players.Add(photonView.Owner.ActorNumber, this);
            var dict = GameManager.Instance.players;
            int key = IsBot ? (100000 + photonView.ViewID) : photonView.OwnerActorNr;
            if (dict.ContainsKey(key)) dict[key] = this;
            else dict.Add(key, this);

            // 해당 클라이언트의 플레이어를 게임매니저에서 가지고 있게끔 추가 (12/17 유빈)
            if (photonView.IsMine && !IsBot)
                GameManager.Instance.playerOfThisClient = this;
        }

        if (!photonView.IsMine) return;
        //playerAction.Enable();

        playerAction.PlayerActionMap.Dash.started += Dash;
        playerAction.PlayerActionMap.Attack.started += AttackStart;
        playerAction.PlayerActionMap.Attack.canceled += AttackCancel;
        playerAction.PlayerActionMap.Grapple.started += GrappleStart;
        playerAction.PlayerActionMap.Grapple.canceled += GrappleCancel;
        playerAction.PlayerActionMap.MouseAppear.started += MouseAppearStart;
        playerAction.PlayerActionMap.MouseAppear.canceled += MouseAppearCancel;
#if UNITY_EDITOR
        playerAction.PlayerActionMap.TestKill.started += TestKill;
        playerAction.PlayerActionMap.FullInk.started += FullInk;
#endif

        //다시 켜질때 무조건 리스폰 장소에서 켜지니까 데미지 않받게
        isDamaged = false;
        // 이걸 왜 외부에서 해줄까.. (튜토리얼에서 여기서 초기화 함)
        if(Status != null)
            SetHealth(Status.MaximumHealth);
        //Debug.Log("데미지 첨에 금지");
        //Debug.Log("OnEnable");

        (UIManager.Instance?.GameHUDUI as GameHUDUI)?.EnsurePlayerListInitialized();
        
        isGimmickActive = false;
    }

    private void FixedUpdate()
    {
        // 대시 불가 sfx 딜레이 세어주기
        warningSoundPlayDelay -= Time.fixedDeltaTime;
    }

    private void OnDisable()
    {
        _inkHandler.OnInkChanged -= UpdateInkFill;

        // GameManager가 존재하면, players 딕셔너리에서 자기 자신을 제거합니다.
        if (GameManager.Instance != null)
        {
            //GameManager.Instance.players.Remove(photonView.Owner.ActorNumber);
            var dict = GameManager.Instance.players;
            int key = IsBot ? (100000 + photonView.ViewID) : photonView.OwnerActorNr;
            if (dict.ContainsKey(key) && dict[key] == this) dict.Remove(key);
        }

        if (!photonView.IsMine) return;
        playerAction.Disable();

        playerAction.PlayerActionMap.Dash.started -= Dash;
        playerAction.PlayerActionMap.Attack.started -= AttackStart;
        playerAction.PlayerActionMap.Attack.canceled -= AttackCancel;
        playerAction.PlayerActionMap.Grapple.started -= GrappleStart;
        playerAction.PlayerActionMap.Grapple.canceled -= GrappleCancel;
        playerAction.PlayerActionMap.MouseAppear.started += MouseAppearStart;
        playerAction.PlayerActionMap.MouseAppear.canceled += MouseAppearCancel;
#if UNITY_EDITOR
        playerAction.PlayerActionMap.TestKill.started -= TestKill;
        playerAction.PlayerActionMap.FullInk.started -= FullInk;
#endif
    }

    // 이벤트 구독 일괄 해제를 위해 추가 (12/15 유빈)
    // 이벤트 구독 해제를 안해주고 파괴 타이밍이 꼬이면 렘 누수가 발생할 수 있음
    private void OnDestroy()
    {
        OnDamageTaken = null;
        OnInkChanged = null;
        OnDeath = null;
        OnDamaged = null;
        OnHealthChanged = null;
        OnBulletSizeChanged = null;
        OnBeforeDeath = null;
        OnBrushSizeChanged = null;
    }

    //갈고리 잉크 소모량 표기
    private void UpdateInkFill(float currentInk)
    {
        if (_injectorRenderer == null) return;
        float ratio = (_inkHandler.maximumInk > 0f)
            ? Mathf.Clamp01(currentInk / _inkHandler.maximumInk)
            : 0f;

        // 실제 잉크 UI와 동기화하기위해 스케일링했습니다. 갈고리 잉크통은 3d 육면체고, UI 잉크표시는 2d 직사각형 면이므로 차오르는 정도의 표기가 달라요.
        float scaledRatio = ratio * 0.5f;
        // 그래프상 반전해야 제대로 잉크가 소모됌.
        scaledRatio = 1f - scaledRatio;

        _injectorRenderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(_fillAmountPropID, scaledRatio);
        _mpb.SetColor(_teamColorPropID, _teamColor);
        _injectorRenderer.SetPropertyBlock(_mpb);
    }

    private void Dash(InputAction.CallbackContext context)
    {
        //if (playerPhotonView.Owner != PhotonNetwork.LocalPlayer) return;
        if (Status.Ink >= dashInkCost)
        {
            if (!isTryGrapple)
            {
                DashMaxSpeedChanger();
                InkEvents.ConsumeInk(playerPhotonView.ViewID, dashInkCost);
                isDash = true;
                ////Debug.Log("dash");
                SoundManager.Instance.PlaySfx(ClipIndex_SFX.mwfx_Dash, 0.5f);
                _emote?.NotifyDash();
            }
        }
        else if (warningSoundPlayDelay < 0)
        {
            // 부스터 사용 불가 경고음 재생
            SoundManager.Instance.PlaySfx(ClipIndex_SFX.mwfx_Warning, volumeScale);
            // 경고음 딜레이 부여
            warningSoundPlayDelay = warningSoundPlayDelay_Played;
        }
    }

    public void DashMaxSpeedChanger()
    {
        // 1. 핵심: 만약 이전에 실행된 시퀀스가 있다면 즉시 중지하고 제거합니다.
        //    이것 덕분에 유지 시간이 초기화됩니다.
        if (mySequence != null && mySequence.IsActive())
        {
            mySequence.Kill();
        }

        // 2. 새로운 시퀀스를 생성합니다.
        mySequence = DOTween.Sequence();

        // 3. 시퀀스에 애니메이션을 순서대로 추가(Append)합니다.
        mySequence.Append(DOTween.To(() => Status.MaximumMoveSpeed, x => Status.MaximumMoveSpeed = x, boostMaxSpeed,
                raiseDuration)) // myFloat을 targetValue로
            .AppendInterval(holdDuration) // holdDuration 만큼 대기
            .Append(DOTween.To(() => Status.MaximumMoveSpeed, x => Status.MaximumMoveSpeed = x, originMaxSpeed,
                lowerDuration)); // myFloat을 originalValue로
    }

    private void AttackStart(InputAction.CallbackContext context)
    {
        //// ▼ 무슨 장치로 눌렸는지 로그 찍기 (범인 색출)
        //Debug.Log($"[입력 감지됨] 장치 이름: {context.control.device.name} / 경로: {context.control.path}");
        
        // ▼▼▼ [추가] 모바일에서 화면 터치(마우스 시뮬레이션)로 인한 발사 방지 ▼▼▼
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        // 입력 장치가 '마우스'(터치)라면 함수를 강제 종료 (공격 안 함)
        // 오직 'Gamepad'(UI 버튼) 신호만 통과됨
        if (context.control.device is UnityEngine.InputSystem.Mouse) return;
#endif
        
        // if (!isTryGrapple) >> 해당 판정 구문은 무기로 이동
        isAttack = true;
    }

    private void AttackCancel(InputAction.CallbackContext context)
    {
        isAttack = false;
    }

    private void GrappleStart(InputAction.CallbackContext context)
    {
        // ▼▼▼ [추가] 모바일에서 실수로 눌리는 것 방지 ▼▼▼
#if UNITY_ANDROID || UNITY_IOS
        if (context.control.device is UnityEngine.InputSystem.Mouse) return;
#endif
        
        isTryGrapple = true;
        SoundManager.Instance.PlaySfx(ClipIndex_SFX.mwfx_Hook, 0.5f);
    }

    private void GrappleCancel(InputAction.CallbackContext context)
    {
        isTryGrapple = false;
    }

    private void MouseAppearStart(InputAction.CallbackContext context)
    {
        if (GameManager.Instance.settingUI.gameObject.activeSelf) return;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
    }

    private void MouseAppearCancel(InputAction.CallbackContext context)
    {
        if (GameManager.Instance.settingUI.gameObject.activeSelf) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    //테스트용 체력 감소
    private void TestKill(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine) return;
        UpdateHealthByDelta(-30f);
        //Debug.Log("내 체력 : " + Status.Health);
    }

    //테스트용 물감 가득 차게
    private void FullInk(InputAction.CallbackContext context)
    {
        if (!photonView.IsMine) return;
        InkEvents.FullInk(playerPhotonView.ViewID);
    }

    public void InitializeStatus(PlayerStatus initialStatus, PlayerInfo playerInfo)
    {
        Status = initialStatus;
        Info = playerInfo;

        //characterRenderer.material.color = playerInfo.TeamColor;

        OnHealthChanged?.Invoke(Status.Health);

        //OnHpUIChanged?.Invoke(Status.Health);  // HP UI만 갱신 추가함
        //OnInkChanged?.Invoke(Status.Ink);      // 기존 Ink 이벤트는 그대로 추가함
        //OnInkChanged?.Invoke(Status.Ink);

        photonView.RPC(nameof(SetLayer), RpcTarget.AllBuffered, Info.TeamId);
        photonView.RPC(nameof(UpdateColor), RpcTarget.AllBuffered,
            Info.TeamId); // AllViaServer로 하면 로컬에서 안바꿔줘도 될 거 같은데 > 테스트 ㄱㄱ

        // 플레이어 팀에 따라 카메라 전방 주시하도록.
        playerCameraController.InitRotation(Info.TeamId);

        //Debug.Log($"[Initialize] {playerInfo.Nickname} " +$"정보: Seq={playerInfo.SequenceNumber}, " +$"정보: TeamID={playerInfo.TeamId}");
    }

    [PunRPC]
    void SetLayer(byte teamID)
    {
        // Info도 같이 맞춰줌(레이어/색/피직스/팀킬 판정 일관)
        if (Info != null) Info.TeamId = teamID;

        // 팀ID에 따라 해당 팀 플레이어에 레이어 부여
        if (Info.TeamId == 0)
        {
            gameObject.layer = LayerMask.NameToLayer(LayerName_TeamA);
        }
        else if (Info.TeamId == 1)
        {
            gameObject.layer = LayerMask.NameToLayer(LayerName_TeamB);
        }
    }

    // 팀컬러 로컬에서만 변화
    private void PlayerColorSync()
    {
        if (Info != null)
            UpdateColor(Info.TeamId);

        // 잉크 게이지의 색상을 팀 색상으로 변경
        Color teamColor = Color.white;
        switch ((Team)Info.TeamId)
        {
            case Team.TeamA:
                teamColor = DataManager.Instance.colorTeamA;
                break;
            case Team.TeamB:
                teamColor = DataManager.Instance.colorTeamB;
                break;
            default:
                break;
        }

        if (TryGetComponent(out PlayerInkHandler inkHandler))
            inkHandler.SetInkBarColor(teamColor);

        //if (photonView.Owner.CustomProperties.TryGetValue(
        //        CustomPropKey.TeamColor, out var colObj)
        //    && colObj is Vector3 v)
        //{
        //    characterRenderer.material.color = new Color(v.x, v.y, v.z);
        //}

        // 3) injectorInk 파츠 컬러링
        _teamColor = teamColor;
        if (_injectorRenderer != null)
        {
            _injectorRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(_teamColorPropID, _teamColor);
            _injectorRenderer.SetPropertyBlock(_mpb);
        }
    }

    public void SetHealth(float newHealth) //추가함
    {
        Status.Health = Mathf.Clamp(newHealth, 0f, Status.MaximumHealth);
        OnHealthChanged?.Invoke(Status.Health);
        //OnHpUIChanged?.Invoke(Status.Health); // HP UI 갱신 추가함
        if (Status.Health <= 0f)
        {
            if (deathEffectPrefab != null)
            {
                photonView.RPC(nameof(RPC_SpawnDeathEffect), RpcTarget.All, transform.position + Vector3.up * 1.5f);
            }

            OnDeath?.Invoke(this);
        }
    }

    [PunRPC]
    public void UpdateHealthByDelta(float delta)
    {
        if (!HasAuthorityForGameplay()) return;
        //Debug.Log($"피격 : {Info.Nickname} {delta}");
        if (isDamaged)
        {
            // 대미지 감소율을 최종 대미지 계산에 추가 (9/17 유빈)
            Status.Health = Mathf.Clamp(Status.Health + delta * Status.DamegeReduction, 0f, Status.MaximumHealth);
        }

        if (delta < 0f)
        {
            // 현재 OnDamaged에는 블러드스크린 UI 표시만 포함-> 봇은 실행할 필요가 없어서 분기 처리(12/26 유빈)
            if (!IsBot)
                OnDamaged?.Invoke();
            _emote?.NotifyHit();
        }

        OnHealthChanged?.Invoke(Status.Health);
        //OnHpUIChanged?.Invoke(Status.Health); // HP UI 갱신 추가함
        if (Status.Health <= 0f)
        {
            SoundManager.Instance.PlaySfx(ClipIndex_SFX.mwfx_Die, 1f);
            _emote?.NotifyDeath();

            if (deathEffectPrefab != null)
            {
                photonView.RPC(nameof(RPC_SpawnDeathEffect), RpcTarget.All, transform.position + Vector3.up * 1.5f);
            }

            ////Debug.Log("피가 없어");
            OnBeforeDeath?.Invoke();
            OnDeath?.Invoke(this);
        }
    }

    [PunRPC]
    public void RPC_SpawnDeathEffect(Vector3 position)
    {
        if (deathEffectPrefab == null) return;

        GameObject effect = Instantiate(deathEffectPrefab, position, Quaternion.identity);

        var ps = effect.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            var main = ps.main;
            main.startColor = characterRenderer.material.color;
        }

        Destroy(effect, 2f);
    }

    [PunRPC]
    public void TakeDamage(float damage, int actorNum, PhotonMessageInfo info)
    {
        if (!HasAuthorityForGameplay())
            return; // 이것도 필요없을지도? 맞은 플레이어 해당 포톤뷰의 Owner에게만 보내는데? >> isMine 설정이 되어 있는데도 문제가 생긴다?
        //Debug.Log($"[DAMAGE] {photonView.Owner.NickName} took {damage} dmg  " + $"(before={Status.Health}, after={Status.Health - damage})");
        ////Debug.Log($"[TakeDamage] {PhotonNetwork.LocalPlayer.NickName} ({photonView.ViewID}) got hit! Called by {info.Sender.NickName}, isMine={photonView.IsMine} hitby {AttackerName} ");
        bool wasAlive = Status.Health > 0f;
        // '맞은 쪽' 클라이언트가 자기 체력을 깎도록
        UpdateHealthByDelta(-damage);

        // 피격 이벤트 발생 (애니메이션 & 표정용)
        OnDamageTaken?.Invoke(Status.Health);

        // 체력이 0 이하가 되고, 이전에는 살아있었다면 → 킬 확정
        if (Status.Health <= 0f && wasAlive)
        {
            // 1) 최근 1초 이내 봇 힌트가 있으면 그 봇 Seq를 킬러로 사용
            int killerToSend = actorNum;
            if (_lastBotAttackerSeq > 0 && (Time.time - _lastBotAttackerAt) <= 1.0f)
                killerToSend = _lastBotAttackerSeq;

            // 2) 피해자 ID: 봇은 Seq, 사람은 ActorNumber
            int victimToSend = IsBot ? Info.SequenceNumber : photonView.OwnerActorNr;

            // 이제 단 한 번의 RPC로 모든 처리를 위임
            RPCManager.Instance.photonView.RPC(
                nameof(RPCManager.RPC_RegisterKill),
                RpcTarget.AllViaServer,
                killerToSend,
                victimToSend);
        }
    }

    [PunRPC]
    void RPC_ShowKillFeed(int killerID, int victimID)
    {
        UIManager.Instance.GameHUDUI.AddKillFeedEntry(killerID, victimID);
    }

    [PunRPC]
    void UpdateColor(byte teamID)
    {
        // 혹시 아직 캐싱이 안 되었다면
        if (characterRenderer == null)
            characterRenderer = GetComponentInChildren<SkinnedMeshRenderer>();

        Team team = (Team)teamID;
        Color teamColor = Color.white;
        switch (team)
        {
            case Team.TeamA:
                teamColor = DataManager.Instance.colorTeamA;
                break;
            case Team.TeamB:
                teamColor = DataManager.Instance.colorTeamB;
                break;
            default:
                break;
        }

        if (characterRenderer == null) return;

        // 머티리얼이 여러 개일 수 있으니 배열로 돌면서
        foreach (var mat in characterRenderer.materials)
        {
            // URP Lit: Base Color 프로퍼티
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", teamColor);
            // Standard: _Color 프로퍼티
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", teamColor);
        }

        // 2) injectorInk 도 컬러 세팅
        _teamColor = teamColor;
        if (_injectorRenderer != null)
        {
            _injectorRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(_teamColorPropID, _teamColor);
            _injectorRenderer.SetPropertyBlock(_mpb);
        }
    }

    // [PunRPC]
    // public void UpdateInkByDelta(float delta)
    // {
    //     Status.Ink = Mathf.Clamp(Status.Ink + delta, 0f, Status.MaximumInk);
    //     OnInkChanged?.Invoke(Status.Ink);
    // }

    //여기 필요한 부분인가?? 게임 메니저에서 플레이어 생성하고 초기화 하는데 여기서도 또 하넹??
    //네 아마도 이건 필요한부분입니다. 빼면 내 컴퓨터에서 다른플레이어들이 생성될때 기본값으로 초기화가 안될거에요.
    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        var inst = photonView.InstantiationData;

        // === 봇: InstantiationData 기반으로 '모든 클라'에서 올바른 초기화 ===
        bool isBot = false;
        if (inst != null && inst.Length >= 5)
        {
            if ((inst[0] is int i0 && i0 == 1) || (inst[0] is byte b0 && b0 == 1))
                isBot = true;
        }

        if (isBot)
        {
            byte teamId = (byte)(inst[1] is int ti ? ti : (byte)inst[1]);
            int weaponIdx = (inst[2] is int wi) ? wi : (int)(byte)inst[2];
            string botId = inst[3] as string ?? $"bot#{photonView.ViewID}";
            string nickname = inst[4] as string ?? "BOT";

            // ❗전 클라에서 동일하게: "bot#XX" 의 XX를 숫자로 뽑아 고정 시퀀스로 사용
            int seqFromId = 0;
            {
                // botId가 "bot#05" 형태라는 가정
                var digits = System.Text.RegularExpressions.Regex.Match(botId, @"\d+").Value;
                if (!int.TryParse(digits, out seqFromId)) seqFromId = photonView.ViewID; // 폴백
            }
            int botSeq = BotService.BotSeqFromId(botId);

            // [FIX] 봇의 모자 인덱스를 Room CustomProperties의 BotList에서 읽어오기
            int hatIndex = -1;
            var bots = BotService.GetBotsFromRoom();
            var botInfo = System.Linq.Enumerable.FirstOrDefault(bots, b => b.Id == botId);
            if (botInfo != null)
            {
                hatIndex = botInfo.HatIndex;
            }

            var initial = new PlayerStatus(100f, 100f, 100f, 100f, 30f, 30f, 30f, 45f, 1);
            var infoObj = new PlayerInfo
            {
                SequenceNumber = 10000 + botSeq, // ✅이제 '클라 공통' 시퀀스
                PlayerId = botId,
                Nickname = nickname,
                TeamId = teamId,
                TeamColorA = 0,
                TeamColorB = 0,
                CharacterSelection = 0,
                WeaponSelection = weaponIdx,
                HatSelection = hatIndex  // [FIX] BotList에서 읽어온 모자 인덱스 사용
            };

            InitializeStatus(initial, infoObj);
            // [FIX] 봇은 photonView.Owner.CustomProperties가 아닌 PlayerHandler.Info를 읽어야 함
            GetComponent<PlayerCosmetics>()?.ApplyFromProps(null);
            return;
        }

        // 플레이어가 생성될 때마다 호출되는 곳
        var props = photonView.Owner.CustomProperties;
        PlayerInfo playerInfo = PlayerInfo.FromHashtable(props);
        // Status는 초기값(로컬과 동일) 혹은 서버에서 받아온 값으로 설정
        var initialStatus = new PlayerStatus(100f, 100f, 100f, 100f, 30f, 30f, 30f, 45f, 1);
        InitializeStatus(initialStatus, playerInfo);
        GetComponent<PlayerCosmetics>()?.ApplyFromProps(photonView.Owner.CustomProperties);
    }

    [PunRPC]
    public void OffPlayer()
    {
        gameObject.SetActive(false);
    }

    [PunRPC]
    public void OnPlayer()
    {
        gameObject.SetActive(true);
    }

    [PunRPC]
    public void EquipWeapon(int weaponIndex)
    {
        // [변경] 혹시 남아 있는 이전 무기들 정리(로컬 호출)
        if (weapon != null || GetComponentInChildren<Weapon_Base>(true) != null)
            DestroyWeapon(); // [변경]

        // 생성할 무기 프리팹 가져오기
        GameObject weaponPrefab = DataManager.Instance.WeaponPrefabs[weaponIndex];
        if (weaponPrefab == null)
        {
            //Debug.LogError("해당 경로에 무기가 없습니다.");
            return;
        }

        // 대기방에서 해당 플레이어가 설정한 무기 생성
        GameObject instantiateWeapon = Instantiate(weaponPrefab, WeaponTransform.position, WeaponTransform.rotation);
        // 플레이어를 부모로
        instantiateWeapon.transform.SetParent(this.transform);
        // 팔 꺾인 거 잡기
        instantiateWeapon.transform.localRotation = Quaternion.Euler(Vector3.zero);
        //무기 변수에 생성한 무기 넣어주기
        weapon = instantiateWeapon.GetComponent<Weapon_Base>();
        //Debug.Log(weapon);
    }

    [PunRPC]
    public void DestroyWeapon()
    {
        // [변경] 널 가드
        if (weapon != null)
        {
            Destroy(weapon.gameObject);
            weapon = null; // [변경] 참조 정리
        }

        // [변경] 혹시 남아있는 중복 무기까지 모두 정리
        var allWeapons = GetComponentsInChildren<Weapon_Base>(true);
        for (int i = 0; i < allWeapons.Length; i++)
        {
            if (allWeapons[i] != null)
                Destroy(allWeapons[i].gameObject);
        }
    }

    //지금 내부 트리거들이 전부 반응 해서 2번 반응하는데 일단 돌아가긴해(창민)
    private void OnTriggerEnter(Collider other)
    {
        if ((Barriers & (1 << other.gameObject.layer)) != 0)
        {
            ////Debug.Log("데미지 금지");
            isDamaged = false;
            //공격도 다시 못 하게
            isAttack = false;
            //공격 금지
            if (playerAction != null)
                playerAction.PlayerActionMap.Attack.started -= AttackStart;

            //Debug.Log(other.name + " 트리거 들어감");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if ((Barriers & (1 << other.gameObject.layer)) != 0)
        {
            //Debug.Log(other.name + " 트리거 나옴");
            //isDamaged = true;
            //공격 허가
            playerAction.PlayerActionMap.Attack.started += AttackStart;

            if (!photonView.IsMine) return;
            bool invincibleNow = (_effectHandler != null && _effectHandler.IsInvincible);

            if (!invincibleNow)
            {
                isDamaged = true;
            }
        }
    }

    public void ForceReleaseGrapple()
    {
        isTryGrapple = false;
    }

    [PunRPC]
    public void RPC_MarkLastBotAttacker(int botSeq)
    {
        _lastBotAttackerSeq = botSeq;
        _lastBotAttackerAt = Time.time;
    }

    // =================== 애니메이션 네트워크 동기화 RPC ===================
    [PunRPC]
    public void RPC_TriggerGrapple()
    {
        var animManager = GetComponentInChildren<CharacterAnimationManager>();
        if (animManager != null)
        {
            animManager.TriggerGrappleAnimation();
        }
    }

    [PunRPC]
    public void RPC_TriggerDash()
    {
        var animManager = GetComponentInChildren<CharacterAnimationManager>();
        if (animManager != null)
        {
            animManager.TriggerDashAnimation();
        }
    }

    [PunRPC]
    public void RPC_TriggerHit()
    {
        var animManager = GetComponentInChildren<CharacterAnimationManager>();
        if (animManager != null)
        {
            animManager.TriggerHitAnimation();
        }
    }

    public bool HasAuthorityForGameplay()
    {
        // 봇이면 현재 마스터가 권한, 사람이면 기존대로 내 소유
        return IsBot ? PhotonNetwork.IsMasterClient : photonView.IsMine;
    }
}