using Photon.Pun;
using System;
using UnityEngine;
using UnityEngine.UI;

public class PlayerInkHandler : MonoBehaviourPun, IPunObservable
{
    private PlayerHandler playerHandler;

    private float currentInkRecoveryRate;
    private float defaultInkRecoveryRate = 3f;
    private float botInkRecoveryRate = 12f; // 봇 전용 자동 충전 속도
    private float BoostInkRecoveryRate = 60f;

    // 실제 잉크 변화 이벤트
    public event Action<float> OnInkChanged;

    //실험용
    public Image inkGage;

    //실험용
    public GameObject inkGageObject;

    public float Ink
    {
        get { return playerHandler.Status.Ink; }
        set
        {
            playerHandler.Status.Ink = value;
            OnInkChanged?.Invoke(playerHandler.Status.Ink);
        }
    }

    public float maximumInk
    {
        get { return playerHandler.Status.MaximumInk; }
        set { playerHandler.Status.MaximumInk = value; }
    }


    private void Awake()
    {
        playerHandler = GetComponent<PlayerHandler>();
    }

    private void OnEnable()
    {
        SubscribeInkEvents();
        //InkEvents.OnInkConsumed += ConsumeInk;
        //InkEvents.OnInkRestored += RestoreInk;
        //InkEvents.OnBoostInkRecovery += BoostInkRecovery;
        //InkEvents.OnResetInkRecovery += ResetInkRecovery;
        //InkEvents.OnFullInk += fullInk;
        //InkEvents.OnItemInkBoostRecovery += ItemInkRecovery;
    }

    private void OnDisable()
    {
        UnsubscribeInkEvents();
        //InkEvents.OnInkConsumed -= ConsumeInk;
        //InkEvents.OnInkRestored -= RestoreInk;
        //InkEvents.OnBoostInkRecovery -= BoostInkRecovery;
        //InkEvents.OnResetInkRecovery -= ResetInkRecovery;
        //InkEvents.OnFullInk -= fullInk;
        //InkEvents.OnItemInkBoostRecovery -= ItemInkRecovery;
    }
    private void OnDestroy()
    {
        UnsubscribeInkEvents();
    }
  
    private void SubscribeInkEvents()
    {
        InkEvents.OnInkConsumed += ConsumeInk;
        InkEvents.OnInkRestored += RestoreInk;
        InkEvents.OnBoostInkRecovery += BoostInkRecovery;
        InkEvents.OnResetInkRecovery += ResetInkRecovery;
        InkEvents.OnFullInk += fullInk;
        InkEvents.OnItemInkBoostRecovery += ItemInkRecovery;
    }
    private void UnsubscribeInkEvents()
    {
        InkEvents.OnInkConsumed -= ConsumeInk;
        InkEvents.OnInkRestored -= RestoreInk;
        InkEvents.OnBoostInkRecovery -= BoostInkRecovery;
        InkEvents.OnResetInkRecovery -= ResetInkRecovery;
        InkEvents.OnFullInk -= fullInk;
        InkEvents.OnItemInkBoostRecovery -= ItemInkRecovery;
    }
    private void Start()
    {
        // 로컬 인간만 잉크 게이지 UI 표시
        if (playerHandler == null) playerHandler = GetComponent<PlayerHandler>();

        bool isLocalHuman = photonView.IsMine && playerHandler != null && !playerHandler.IsBot;

        // 봇은 12 잉크/초, 플레이어는 3 잉크/초
        currentInkRecoveryRate = (playerHandler != null && playerHandler.IsBot)
            ? botInkRecoveryRate
            : defaultInkRecoveryRate;

        if (inkGageObject != null)
            inkGageObject.SetActive(isLocalHuman);

        if (isLocalHuman)
            OnInkChanged?.Invoke(Ink);

        // 치명적 버그 수정: PhotonView Observed Components에 자신 추가 (안하면 injectorInk 동기화 안됨!)
        if (photonView != null && !photonView.ObservedComponents.Contains(this))
        {
            photonView.ObservedComponents.Add(this);
            Debug.Log($"[PlayerInkHandler] Added to PhotonView.ObservedComponents for {gameObject.name} (IsBot={playerHandler?.IsBot})");
        }

        // 봇의 경우 즉시 injectorInk 업데이트 (초기 상태 반영)
        if (playerHandler != null && playerHandler.IsBot)
        {
            OnInkChanged?.Invoke(Ink);
        }
    }

    private void Update()
    {
        if (!photonView.IsMine || playerHandler == null) return;

        // 플레이어만 UI 업데이트
        if (!playerHandler.IsBot)
        {
            UpdateInkGage();
        }

        // 플레이어와 봇 모두 자동 충전 (공격/대쉬 중이 아닐 때)
        if (!playerHandler.isAttack && !playerHandler.isDash)
        {
            RecoverInk();
        }
    }

    private void RecoverInk()
    {
        // 시간 기반 회복
        Ink += currentInkRecoveryRate * Time.deltaTime;

        // Clamp 처리
        playerHandler.Status.Ink = Mathf.Clamp(Ink, 0f, maximumInk);
    }

    public void ConsumeInk(int sourceViewId, float amount)
    {
        if (this == null) return;
        if (photonView.ViewID != sourceViewId) return;
        Ink = Mathf.Clamp(Ink - amount, 0f, maximumInk);
    }

    public void RestoreInk(int sourceViewId, float amount)
    {
        if (this == null) return;
        if (photonView.ViewID != sourceViewId) return;
        Ink = Mathf.Clamp(Ink + amount, 0f, maximumInk);
    }

    private void BoostInkRecovery(int sourceViewId)
    {
        if (this == null) return;
        if (photonView.ViewID != sourceViewId) return;
        currentInkRecoveryRate = BoostInkRecoveryRate;
    }

    private void ItemInkRecovery(int sourceViewId, float amount)
    {
        if (this == null) return;
        if (photonView.ViewID != sourceViewId) return;
        Ink = Mathf.Clamp(Ink + amount, 0f, maximumInk);
    }

    private void ResetInkRecovery(int sourceViewId)
    {
        if (this == null) return;
        if (photonView.ViewID != sourceViewId) return;
        currentInkRecoveryRate = defaultInkRecoveryRate;
    }

    //실험용
    private void fullInk(int sourceViewId)
    {
        if (this == null) return;
        if (photonView.ViewID != sourceViewId) return;
        Ink = maximumInk;
    }

    private void UpdateInkGage()
    {
        Vector3 scale = inkGage.transform.localScale;
         scale.x = Ink / maximumInk;
        inkGage.transform.localScale = scale;
    }

    public void SetInkBarColor(Color color)
    {
        inkGage.color = color;
    }


    //내 잉크량 상대방이 알아야했나요?(창민)
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // 내 로컬 잉크값을 다른 클라이언트로 보낸다
            stream.SendNext(Ink);
        }
        else
        {
            // 다른 클라이언트에서 보내온 잉크값을 받아서 적용
            float received = (float)stream.ReceiveNext();
            Ink = Mathf.Clamp(received, 0f, maximumInk);
        }
    }
}