using Photon.Pun;
//using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
//using UnityEngine.UI;

[RequireComponent(typeof(PlayerHandler))]
public class PlayerController : MonoBehaviourPun
{
    private PlayerInfo playerInfo;
    private PlayerStatus playerStatus;
    private PlayerHandler playerHandler;
    private PlayerAction playerAction;

    private PlayerCameraController playerCameraController;

    public Rigidbody playerRigidbody;
    private bool dashPressed = false;
    private Vector3 HorizontalityMoveDirection;
    private Vector3 activeMoveForce;
    private Vector3 lastMoveDirection = Vector3.forward;
    private Quaternion targetRotation;
    private Vector3 cameraForward;
    private Vector3 cameraRight;
    private Vector3 cameraUp;

    private Vector2 horizontalityMoveInput;
    private Vector2 VerticalMoveInput;

    LayerMask myteam;

    //플레이어 아이템 이펙트 캐싱 - 조작반전 기능 때문
    private PlayerEffectHandler _effectHandler;

    private EmoteFaceController _emote;

    // ▼▼▼모바일 전용 설정 변수 ▼▼▼
    [Header("Mobile Flight Settings")] public float mobileTurnSensitivity = 150f;

    private Transform camTransform;
    private Vector2 Mobile_Input;

    //모바일 UI 오브젝트를 넣을 빈칸을 만듦
    //public List<GameObject> mobileControllerUI = new List<GameObject>();

    //신규 Inkoid 용 변수, 이제 위아래 키 입력시에 캐릭터가 회전하지않도록 하기위함. 애니메이션으로 제어한다.
    private Vector3 lastPlanarMoveDirection = Vector3.forward; // y=0인 마지막 수평 이동 방향
    private const float DIR_EPS = 0.0001f;

    private void Awake()
    {
        playerRigidbody = GetComponent<Rigidbody>();
        playerHandler = GetComponent<PlayerHandler>();
        _emote = GetComponentInChildren<EmoteFaceController>(true);
    }

    private void Start()
    {
        playerAction = playerHandler.playerAction;
        playerCameraController = playerHandler.playerCameraController;
        _effectHandler = GetComponent<PlayerEffectHandler>();
        myteam = LayerMask.GetMask(LayerMask.LayerToName((int)EnumLayer.LayerType.Weapon),
            LayerMask.LayerToName(gameObject.layer));

#if UNITY_ANDROID || UNITY_IOS
        // 1. 카메라 찾기
        if (playerCameraController != null && playerCameraController.playerVirtualCamera != null)
        {
            camTransform = playerCameraController.playerVirtualCamera.transform;
        }
        // 2. UI 활성화/비활성화 (List 사용)
        // bool isLocalHuman = photonView.IsMine && !playerHandler.IsBot;

        // if (mobileControllerUI != null && mobileControllerUI.Count > 0)
        // {
        //     // 리스트에 있는 모든 UI 오브젝트를 하나씩 꺼내서 설정
        //     foreach (GameObject uiPart in mobileControllerUI)
        //     {
        //         if (uiPart != null)
        //         {
        //             // A. 내 거면 켜고, 아니면 끔
        //             uiPart.SetActive(isLocalHuman);
        //             
        //             // B. [중요] 내 캐릭터가 아니면 터치 감지(Raycast Target)를 강제로 끔
        //             // (투명한 UI가 내 화면을 가려서 터치를 막는 현상 방지)
        //             if (!isLocalHuman)
        //             {
        //                 Graphic[] graphics = uiPart.GetComponentsInChildren<Graphic>(true);
        //                 foreach (Graphic g in graphics)
        //                 {
        //                     g.raycastTarget = false;
        //                 }
        //             }
        //         }
        //     }
        // }
#else
        // // 3. PC 버전 안전장치
        // // PC에서는 모바일 UI가 켜져 있으면 안 되므로 리스트에 있는 걸 다 꺼버림
        // if (mobileControllerUI != null && mobileControllerUI.Count > 0)
        // {
        //     foreach (GameObject uiPart in mobileControllerUI)
        //     {
        //         if (uiPart != null)
        //         {
        //             uiPart.SetActive(false);
        //         }
        //     }
        // }
#endif
    }

    private void Update()
    {
        // ▼▼▼ [수정] Alt 키 대신 'T' 키를 사용 (충돌 방지) ▼▼▼
#if UNITY_EDITOR
        // 키보드가 연결되어 있고, T 키를 눌렀을 때
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            Debug.Log("테스트용 마우스 토글 실행됨!"); // 콘솔창 확인용

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None; // 마우스 풀기 (보임)
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked; // 마우스 잠그기 (숨김)
                Cursor.visible = false;
            }
        }
#endif
        // ▲▲▲ [끝] ▲▲▲
    }

    private void FixedUpdate()
    {
        //Debug.Log("현재 UP 키: " + playerAction.PlayerActionMap.VerticalMove.bindings[1].effectivePath +
        //          ", 현재 DOWN 키: " + playerAction.PlayerActionMap.VerticalMove.bindings[2].effectivePath);

        if (!IsPhotonViewIsMine()) return;

        // ▼▼▼ 플랫폼에 따라 다른 이동 함수 실행 ▼▼▼
#if UNITY_ANDROID || UNITY_IOS
        MoveMobile(); // 모바일: 자동 전진 + 스틱 회전
#endif

#if !UNITY_ANDROID && !UNITY_IOS
        Move();             // PC: 키보드 이동 + 카메라 기준
#endif

        if (playerHandler.isDash)
        {
            Dash();
            playerHandler.isDash = false;
        }

        // if (playerHandler.isAttack)
        // {
        //     Attack();
        //     playerHandler.isAttack = false;
        // }
    }

    private void LateUpdate()
    {
        //내 캐릭터가 아니면 회전/카메라 로직을 실행하지 않음!
        if (!photonView.IsMine) return;

        // 수평(평면) 방향만 회전에 사용 (상하 입력으로 인한 180도 튐 방지)
        Vector3 planarMove = new Vector3(activeMoveForce.x, 0f, activeMoveForce.z);

        // 마지막 "수평" 방향만 저장 (대시/기타에서 안정적으로 쓰기 위함)
        if (planarMove.sqrMagnitude > DIR_EPS)
        {
            lastPlanarMoveDirection = planarMove;
        }

        // 1) 공격 시: 카메라 전방(평면)으로만 회전 (pitch 영향 제거)
        if (playerHandler.isAttack)
        {
            Vector3 flatCameraForward = cameraForward; // Move()에서 이미 y=0 처리해두고 있음
            flatCameraForward.y = 0f;

            if (flatCameraForward.sqrMagnitude < DIR_EPS)
                flatCameraForward = lastPlanarMoveDirection;

            targetRotation = Quaternion.LookRotation(flatCameraForward.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 2f * Time.deltaTime);
            return;
        }

#if !UNITY_ANDROID && !UNITY_IOS

        // 2) 수평 이동이 있을 때만: 이동 방향으로 yaw 회전
        if (planarMove.sqrMagnitude > DIR_EPS)
        {
            targetRotation = Quaternion.LookRotation(planarMove.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
        }
        // 3) 수평 이동이 없고(=WASD 개입 없음) 상하만 있거나 완전 대기: yaw 유지 + 기울기 제거
        else
        {
            float currentYaw = transform.eulerAngles.y;
            Quaternion uprightRotation = Quaternion.Euler(0f, currentYaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, uprightRotation, 10f * Time.deltaTime);
        }

        //PC_rotation();
#endif

#if UNITY_ANDROID || UNITY_IOS
        Rotation_Mobile();
#endif
    }

#if !UNITY_ANDROID && !UNITY_IOS
    private void Move()
    {
        // 1.이동방향 입력 받기
        horizontalityMoveInput = playerAction.PlayerActionMap.HorizontalMove.ReadValue<Vector2>();
        VerticalMoveInput = playerAction.PlayerActionMap.VerticalMove.ReadValue<Vector2>();

        if (_effectHandler != null && _effectHandler.AreControlsInverted)
        {
            horizontalityMoveInput *= -1f;
            VerticalMoveInput *= -1f;
        }

        // 2. 카메라 기준 방향 설정
        cameraForward = playerCameraController.playerVirtualCamera.transform.forward;
        cameraRight = playerCameraController.playerVirtualCamera.transform.right;
        //cameraUp = transform.position;

        // y축 방향 제거 (지면 기준 방향으로)
        cameraForward.y = 0;
        cameraForward.Normalize();
        cameraRight.y = 0;
        cameraRight.Normalize();

        // 3. 카메라 기준 이동 방향 계산
        HorizontalityMoveDirection = cameraForward * horizontalityMoveInput.y
                                     + cameraRight * horizontalityMoveInput.x;

        // 4. 최종 이동 벡터
        activeMoveForce = HorizontalityMoveDirection.normalized
                          * playerHandler.Status.HorizontalityMoveSpeed
                          + Vector3.up * (VerticalMoveInput.y * playerHandler.Status.VerticalMoveSpeed);

        // 6. 이동 적용
        playerRigidbody.AddForce(activeMoveForce, ForceMode.Force);

        // 7. 최대 속도 제한
        if (!playerHandler.isGimmickActive)
        {
            if (playerRigidbody.velocity.magnitude > playerHandler.Status.MaximumMoveSpeed)
            {
                playerRigidbody.velocity = playerRigidbody.velocity.normalized
                                           * playerHandler.Status.MaximumMoveSpeed;
            }
        }

        // 이하 주석 처리된 구문들은 LateUpdate()로 작동 시점 이동 (CameraController의 작동 시점을 LateUpdate()로 옮김)
        //// 8. 마지막 방향 저장
        //if (activeMoveForce != Vector3.zero)
        //{
        //    lastMoveDirection = activeMoveForce;
        //}

        ////9.공격할 때 카메라 방향으로 회전
        //if (playerHandler.isAttack)
        //{
        //    targetRotation = Quaternion.LookRotation(cameraForward);
        //    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 2f * Time.fixedDeltaTime);
        //}
        ////이동 방향이 있고 공격 안할 때 이동 방향으로 회전
        //else if (activeMoveForce != Vector3.zero && !playerHandler.isAttack)
        //{
        //    targetRotation =
        //        Quaternion.LookRotation(activeMoveForce, playerCameraController.playerVirtualCamera.transform.up);
        //    transform.rotation = Quaternion.Slerp
        //        (transform.rotation, targetRotation, 10f * Time.fixedDeltaTime);
        //    //transform.rotation = targetRotation;
        //}
        ////대기할 때 자세 초기화
        //else if (!playerHandler.isAttack)
        //{
        //    Vector3 currentEuler = transform.rotation.eulerAngles;
        //    Quaternion uprightRotation = Quaternion.Euler(0f, currentEuler.y, 0f); // 기울기 제거
        //    transform.rotation = Quaternion.Slerp(transform.rotation, uprightRotation, 10f * Time.fixedDeltaTime);
        //}
    }

    private void PC_rotation()
    {
        // ▼▼▼PC에서만 자동 회전 로직 실행 (모바일은 제외) ▼▼▼
        //이동 방향이 있고 공격 안할 때 이동 방향으로 회전
        if (activeMoveForce != Vector3.zero && !playerHandler.isAttack)
        {
            targetRotation =
                Quaternion.LookRotation(activeMoveForce, playerCameraController.playerVirtualCamera.transform.up);
            transform.rotation = Quaternion.Slerp
                (transform.rotation, targetRotation, 10f * Time.fixedDeltaTime);
            //transform.rotation = targetRotation;
        }
        //대기할 때 자세 초기화
        else if (!playerHandler.isAttack)
        {
            Vector3 currentEuler = transform.rotation.eulerAngles;
            Quaternion uprightRotation = Quaternion.Euler(0f, currentEuler.y, 0f); // 기울기 제거
            transform.rotation = Quaternion.Slerp(transform.rotation, uprightRotation, 10f * Time.fixedDeltaTime);
        }
    }
#endif

    // ▼▼▼ [추가] 모바일 전용 이동 함수 (전처리기 포함) ▼▼▼
#if UNITY_ANDROID || UNITY_IOS
    private void MoveMobile()
    {
        // 1. 조이스틱 입력 받기
        Mobile_Input = playerAction.PlayerActionMap.HorizontalMove.ReadValue<Vector2>();

        if (_effectHandler != null && _effectHandler.AreControlsInverted)
            Mobile_Input *= -1f;

        // 입력이 있을 때만 작동 (손을 떼면 멈춤)
        if (Mobile_Input.sqrMagnitude > 0.01f)
        {
            // A. 카메라 회전 처리 (조이스틱 방향대로 시점 이동)
            // -----------------------------------------------------------
            float rotX = Mobile_Input.x * mobileTurnSensitivity * Time.fixedDeltaTime;
            float rotY = Mobile_Input.y * mobileTurnSensitivity * Time.fixedDeltaTime;

            // PlayerCameraController에 만들었던 함수 호출
            playerCameraController.AddMobileInput(rotX, rotY);

            // 안전장치: 혹시 camTransform이 비어있으면 다시 찾아라!
            if (camTransform == null && playerCameraController.playerVirtualCamera != null)
                camTransform = playerCameraController.playerVirtualCamera.transform;
            
            // C. 이동 처리 (카메라 정면으로 자동 전진)
            // -----------------------------------------------------------
            // 조이스틱을 밀고 있으면 무조건 "카메라 정면"으로 힘을 가함
            // 카메라가 확실히 있을 때만 힘을 가해라 (에러 방지)
            if (camTransform != null)
            {
                activeMoveForce = camTransform.forward * playerHandler.Status.HorizontalityMoveSpeed;
                playerRigidbody.AddForce(activeMoveForce, ForceMode.Force);
            }
        }
        else
        {
            // D. 조작이 없으면 정지 (관성 제동)
            // -----------------------------------------------------------
            // 손을 떼면 미끄러지지 않고 금방 멈추게 함
            playerRigidbody.velocity = Vector3.Lerp(playerRigidbody.velocity, Vector3.zero, Time.fixedDeltaTime * 5f);
        }

        // 3. 최대 속도 제한
        if (!playerHandler.isGimmickActive)
        {
            if (playerRigidbody.velocity.magnitude > playerHandler.Status.MaximumMoveSpeed)
            {
                playerRigidbody.velocity = playerRigidbody.velocity.normalized * playerHandler.Status.MaximumMoveSpeed;
            }
        }
    }

    private void Rotation_Mobile()
    {
        // 입력이 있을 때만 작동 (손을 떼면 멈춤)
        if (Mobile_Input.sqrMagnitude > 0.01f)
        {
            
            // 안전장치: 없을 때만 찾아라
            if (camTransform == null && playerCameraController.playerVirtualCamera != null)
            {
                // B. 플레이어 회전 처리 (카메라와 일치시키기)
                // -----------------------------------------------------------
                camTransform = playerCameraController.playerVirtualCamera.transform;
            }

            if (camTransform != null)
            {
                // 캐릭터가 즉시 카메라가 보는 방향을 보게 함 (비행 느낌을 위해 Slerp로 부드럽게)
                transform.rotation =
                    Quaternion.Slerp(transform.rotation, camTransform.rotation, 15f * Time.fixedDeltaTime);

                // 대시 방향 갱신
                lastMoveDirection = camTransform.forward;
            }
        }
    }
#endif


    private void Dash()
    {
        playerHandler.Status.MaximumMoveSpeed += playerHandler.Status.DashForce;

        // 드릴 무기 사용 중 대시는 에임 방향이어야 하기에 if문 분기 처리(10/1 유빈) >> 대시가 없어지면 해당 분기 불필요
        if (playerHandler.isDrillUsing)
        {
            RotateToAimDirection();
            playerRigidbody.AddForce(transform.forward
                                     * playerHandler.Status.DashForce, ForceMode.Impulse);
        }
        else
        {
            Vector3 dashDir = lastPlanarMoveDirection;
            dashDir.y = 0f;

            if (dashDir.sqrMagnitude < DIR_EPS)
                dashDir = transform.forward; // 혹시 모를 안전장치

            playerRigidbody.AddForce(dashDir.normalized * playerHandler.Status.DashForce, ForceMode.Impulse);
        }

        playerHandler.Status.MaximumMoveSpeed -= playerHandler.originMaxSpeed;
    }

    // private void Attack()
    // {
    //     
    // }

    private bool IsPhotonViewIsMine()
    {
        return photonView.IsMine;
        //return true;
    }

    // 플레이어가 드릴 사용 중 aim 방향으로 돌진하게 하기 위한 메서드
    // Weapon_Base.cs의 UpdateCameraWeaponSync() 참고하여 제작
    internal void RotateToAimDirection()
    {
        // 카메라의 위치와 정면 방향을 가져옵니다.
        Transform cameraTransform = playerHandler.playerCameraController.playerVirtualCamera.transform;
        Vector3 cameraPosition = cameraTransform.position;
        Vector3 cameraDirection = cameraTransform.forward;

        // Ray를 생성합니다.
        Ray flontCameraRay = new Ray(playerHandler.transform.position
                                     + playerCameraController.cameraHeightOffset, cameraDirection);
        float maxDistance = 1000f; // Ray의 최대 길이를 적절히 설정하는 것이 좋습니다. (10000f는 너무 길 수 있습니다)

        // Raycast를 실행합니다.
        if (Physics.Raycast(flontCameraRay, out RaycastHit hit, maxDistance, ~myteam))
        {
            // Raycast가 myTeamLayer붙은 오브젝트가 맞았을 경우:
            // 카메라 위치에서 충돌 지점까지 파란색 선을 그립니다.
            ////Debug.DrawLine(cameraPosition, hit.point, Color.blue,10f);

            //     // 거리가 충분하면: 총구가 레이 맞은 위치를 바라보게 함
            // 1. 플레이어에서 목표 지점(hit.point)까지의 '방향' 벡터를 계산합니다.
            Vector3 directionToHit = hit.point - transform.position;

            // 2. 플레이어가 계산된 방향을 바라보도록 회전시킵니다.
            transform.rotation = Quaternion.LookRotation(directionToHit);
        }
        else
        {
            // Raycast가 아무것도 맞추지 못했을 경우:
            // 카메라 위치에서 최대 거리까지 빨간색 선(Ray)을 그립니다.
            ////Debug.DrawRay(cameraPosition, cameraDirection * maxDistance, Color.red,10f);

            // 레이가 아무것에도 맞지 않았을 경우
            // flontCameraRay의 시작점으로부터 maxDistance만큼 떨어진 지점을 목표로 설정
            Vector3 endPoint = flontCameraRay.GetPoint(maxDistance);
            transform.rotation = Quaternion.LookRotation(endPoint - transform.position);
        }
        // 보너스: 총구가 현재 어느 방향을 향하고 있는지 초록색 선으로 표시합니다.
        ////Debug.DrawRay(gunModel.position, gunModel.forward * 5f, Color.green,10f);
    }
}