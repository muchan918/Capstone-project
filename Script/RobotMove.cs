using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class RobotMove : MonoBehaviour
{
    public float moveSpeed = 2f;
    [SerializeField] private float placeDetachDelay = 2.17f; // Putting Down���� ���� ���� Ÿ�̹�(��)

    private NavMeshAgent agent;
    private Vector3 targetPosition;
    private GameObject pendingTarget;

    private Animator anim;
    public RobotPick pick;

    enum RobotState { Idle, Move, Pick, PMove, Picking, Place }
    private RobotState currentState = RobotState.Idle;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        anim = GetComponentInChildren<Animator>();
        pick = GetComponent<RobotPick>();

        if (pick != null) { pick.OnAttach += HandlePickedAttached; pick.OnPlace += HandlePlaced; }
    }
    void OnDestroy()
    {
        if (pick != null) { pick.OnAttach -= HandlePickedAttached; pick.OnPlace -= HandlePlaced; }
    }

    void HandlePickedAttached() { currentState = RobotState.Picking; }
    void HandlePlaced()
    {
        currentState = RobotState.Idle;
        if (anim) anim.SetTrigger("PlaceToIdle");
        pendingTarget = null;
    }

    void Update()
    {
        switch (currentState)
        {
            case RobotState.Move:
                if (Arrived()) { currentState = RobotState.Idle; if (anim) anim.SetTrigger("MoveToIdle"); }
                break;
            case RobotState.PMove:
                if (Arrived()) { currentState = RobotState.Picking; if (anim) anim.SetTrigger("PMoveToPicking"); }
                break;
        }
    }

    bool Arrived() =>
        !agent.pathPending &&
        agent.remainingDistance <= agent.stoppingDistance &&
        (!agent.hasPath || agent.velocity.sqrMagnitude < 0.5f);

    // �̵� ����(�״��)
    public void MoveTo(Vector3 dest)
    {
        targetPosition = dest;
        agent.SetDestination(targetPosition);
        if (currentState == RobotState.Idle && anim) anim.SetTrigger("IdleToMove");
        currentState = RobotState.Move;
    }
    public void MoveForward() => MoveTo(transform.position + transform.forward * 2f);
    public void MoveWhileHolding(Vector3 destination)
    {
        if (pick == null || !pick.IsHolding) return;
        targetPosition = destination;
        agent.SetDestination(destination);
        if (anim) anim.SetTrigger("PickingToPMove");
        currentState = RobotState.PMove;
    }

    // === place on ���� API ===
    public bool PlaceOn(GameObject target)
    {
        if (pick == null || !pick.IsHolding) { Debug.Log("no object in hand"); return false; }
        if (currentState != RobotState.Picking) { Debug.Log("not in Picking state"); return false; }

        currentState = RobotState.Place;
        if (anim) anim.SetTrigger("PickingToPlace");   // Place �ִ� ����

        pendingTarget = target;
        StartCoroutine(CoPlaceAfterDelay());           // �ִ� Ÿ�̹� �� ���� ��������
        return true;
    }

    private IEnumerator CoPlaceAfterDelay()
    {
        yield return new WaitForSeconds(placeDetachDelay);

        if (pick == null || !pick.IsHolding)
        {
            currentState = RobotState.Picking;
            if (anim) anim.SetTrigger("PlaceFailToPicking"); // Animator�� ������ ���
            yield break;
        }

        bool ok = pick.PlaceOn(pendingTarget);        // ���⼭ OnPlace �� HandlePlaced ȣ��
        if (!ok)
        {
            currentState = RobotState.Picking;
            if (anim) anim.SetTrigger("PlaceFailToPicking");
        }
    }
}
