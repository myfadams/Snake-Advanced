using System;
using UnityEditor;
using UnityEngine;

namespace ithappy.Animals_FREE
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class CreatureMover : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField]
        private float m_WalkSpeed = 1f;
        [SerializeField]
        private float m_RunSpeed = 4f;
        [SerializeField, Range(0f, 360f)]
        private float m_RotateSpeed = 90f;
        [SerializeField]
        private Space m_Space = Space.Self;
        [SerializeField]
        private float m_JumpHeight = 5f;

        [Header("Animator")]
        [SerializeField]
        private string m_VerticalID = "Vert";
        [SerializeField]
        private string m_StateID = "State";
        [SerializeField]
        private LookWeight m_LookWeight = new(1f, 0.3f, 0.7f, 1f);

        private Transform m_Transform;
        private CharacterController m_Controller;
        private Animator m_Animator;

        private MovementHandler m_Movement;
        private AnimationHandler m_Animation;

        private Vector2 m_Axis;
        private Vector3 m_Target;
        private bool m_IsRun;

        private bool m_IsMoving;

        public Vector2 Axis => m_Axis;
        public Vector3 Target => m_Target;
        public bool IsRun => m_IsRun;

        private void OnValidate()
        {
            m_WalkSpeed = Mathf.Max(m_WalkSpeed, 0f);
            m_RunSpeed = Mathf.Max(m_RunSpeed, m_WalkSpeed);

            m_Movement?.SetStats(m_WalkSpeed, m_RunSpeed, m_RotateSpeed, m_JumpHeight, m_Space);
        }

        public Space SpaceMode
        {
            get => m_Space;
            set
            {
                m_Space = value;
                m_Movement?.SetStats(m_WalkSpeed, m_RunSpeed, m_RotateSpeed, m_JumpHeight, m_Space);
            }
        }

        public float WalkSpeed
        {
            get => m_WalkSpeed;
            set
            {
                m_WalkSpeed = Mathf.Max(0f, value);
                m_Movement?.SetStats(m_WalkSpeed, m_RunSpeed, m_RotateSpeed, m_JumpHeight, m_Space);
            }
        }

        public float RunSpeed
        {
            get => m_RunSpeed;
            set
            {
                m_RunSpeed = Mathf.Max(m_WalkSpeed, value);
                m_Movement?.SetStats(m_WalkSpeed, m_RunSpeed, m_RotateSpeed, m_JumpHeight, m_Space);
            }
        }

        public float RotateSpeed
        {
            get => m_RotateSpeed;
            set
            {
                m_RotateSpeed = Mathf.Clamp(value, 0f, 720f);
                m_Movement?.SetStats(m_WalkSpeed, m_RunSpeed, m_RotateSpeed, m_JumpHeight, m_Space);
            }
        }

        public CharacterController Controller => m_Controller;

        public void MoveInDirection(Vector3 direction, bool isRun)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                Stop();
                return;
            }

            Vector3 target = m_Transform.position + direction.normalized * 5f;
            SetInput(new Vector2(0f, 1f), target, isRun, false);
        }

        public void MoveTowards(Vector3 targetPosition, bool isRun)
        {
            Vector3 diff = targetPosition - m_Transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude < 0.0001f)
            {
                Stop();
                return;
            }

            SetInput(new Vector2(0f, 1f), targetPosition, isRun, false);
        }

        public void Stop()
        {
            SetInput(Vector2.zero, m_Transform != null ? m_Transform.position + m_Transform.forward : Vector3.forward, false, false);
        }

        private void Awake()
        {
            m_Transform = transform;
            m_Controller = GetComponent<CharacterController>();
            m_Animator = GetComponent<Animator>();

            // Adjust CharacterController center and skinWidth so animal feet sit flush on floor surface
            if (m_Controller != null)
            {
                m_Controller.skinWidth = 0.015f;
                m_Controller.minMoveDistance = 0f;
                float halfH = Mathf.Max(m_Controller.radius, m_Controller.height * 0.5f);
                m_Controller.center = new Vector3(m_Controller.center.x, halfH + m_Controller.skinWidth, m_Controller.center.z);
            }

            // Ensure all skinned meshes update even if bounds are not yet calculated or offscreen
            SkinnedMeshRenderer[] smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < smrs.Length; i++)
            {
                if (smrs[i] != null)
                {
                    smrs[i].updateWhenOffscreen = true;
                }
            }

            m_Space = Space.Self;
            m_Movement = new MovementHandler(m_Controller, m_Transform, m_WalkSpeed, m_RunSpeed, m_RotateSpeed, m_JumpHeight, Space.Self);
            m_Animation = new AnimationHandler(m_Animator, m_VerticalID, m_StateID);

            InitializeAnimator();
        }

        private void Start()
        {
            if (m_Controller != null && m_Controller.enabled)
            {
                m_Controller.Move(Vector3.down * 0.05f);
            }
        }

        public void InitializeAnimator()
        {
            if (m_Animator != null)
            {
                m_Animator.enabled = true;
                m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                m_Animator.SetFloat(m_VerticalID, 0f);
                m_Animator.SetFloat(m_StateID, 0f);
                // Sample idle animation pose immediately on frame 0 so mesh never shows raw FBX bind/T-pose
                m_Animator.Update(0.02f);
            }
        }

        public void ForceIdleAnimation()
        {
            m_Axis = Vector2.zero;
            m_IsMoving = false;
            m_IsRun = false;
            m_Animation?.ResetToIdle();
            if (m_Animator != null && m_Animator.enabled)
            {
                m_Animator.SetFloat(m_VerticalID, 0f);
                m_Animator.SetFloat(m_StateID, 0f);
            }
        }

        private void Update()
        {
            m_Movement.Move(Time.deltaTime, in m_Axis, in m_Target, m_IsRun, m_IsMoving, out var animAxis, out var isAir);
            m_Animation.Animate(in animAxis, m_IsRun ? 1f : 0f, Time.deltaTime);
        }

        private void OnAnimatorIK()
        {
            m_Animation.AnimateIK(in m_Target, m_LookWeight);
        }

        public void SetInput(in Vector2 axis, in Vector3 target, in bool isRun, in bool isJump)
        {
            m_Axis = axis;
            m_Target = target;
            m_IsRun = isRun;

            if (m_Axis.sqrMagnitude < Mathf.Epsilon)
            {
                m_Axis = Vector2.zero;
                m_IsMoving = false;
            }
            else
            {
                m_Axis = Vector3.ClampMagnitude(m_Axis, 1f);
                m_IsMoving = true;
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > m_Controller.stepOffset)
            {
                m_Movement.SetSurface(hit.normal);
            }
        }

        [Serializable]
        private struct LookWeight
        {
            public float weight;
            public float body;
            public float head;
            public float eyes;

            public LookWeight(float weight, float body, float head, float eyes)
            {
                this.weight = weight;
                this.body = body;
                this.head = head;
                this.eyes = eyes;
            }
        }

        #region Handlers
        private class MovementHandler
        {
            private readonly CharacterController m_Controller;
            private readonly Transform m_Transform;

            private float m_WalkSpeed;
            private float m_RunSpeed;
            private float m_RotateSpeed;

            private Space m_Space;

            private float m_TargetAngle;
            private bool m_IsRotating = false;

            private Vector3 m_Normal = Vector3.up;
            private Vector3 m_GravityAcelleration = Physics.gravity;

            private float m_jumpTimer;
            private Vector3 m_LastForward;

            public MovementHandler(CharacterController controller, Transform transform, float walkSpeed, float runSpeed, float rotateSpeed, float jumpHeight, Space space)
            {
                m_Controller = controller;
                m_Transform = transform;

                m_WalkSpeed = walkSpeed;
                m_RunSpeed = runSpeed;
                m_RotateSpeed = rotateSpeed;

                m_Space = space;
                m_Normal = Vector3.up;
                m_LastForward = (transform != null) ? transform.forward : Vector3.forward;
            }

            public void SetStats(float walkSpeed, float runSpeed, float rotateSpeed, float jumpHeight, Space space)
            {
                m_WalkSpeed = walkSpeed;
                m_RunSpeed = runSpeed;
                m_RotateSpeed = rotateSpeed;

                m_Space = space;
            }

            public void SetSurface(in Vector3 normal)
            {
                if (normal.sqrMagnitude > 0.001f)
                {
                    m_Normal = normal.normalized;
                }
                else
                {
                    m_Normal = Vector3.up;
                }
            }

            public void Move(float deltaTime, in Vector2 axis, in Vector3 target, bool isRun, bool isMoving, out Vector2 animAxis, out bool isAir)
            {
                var cameraLook = Vector3.Normalize(target - m_Transform.position);
                if (float.IsNaN(cameraLook.x) || cameraLook.sqrMagnitude < 0.001f)
                {
                    cameraLook = (m_Transform != null) ? m_Transform.forward : Vector3.forward;
                }

                ConvertMovement(in axis, in cameraLook, out var movement);
                if (movement.sqrMagnitude > 0.01f)
                {
                    m_LastForward = Vector3.Normalize(movement);
                }

                var targetForward = (movement.sqrMagnitude > 0.01f) ? m_LastForward : cameraLook;

                // 1. Turn to face desired heading
                Turn(in targetForward, isMoving);
                UpdateRotation(deltaTime);

                // 2. Displace along facing forward vector when moving to prevent sideways sliding/gliding
                CaculateGravity(deltaTime, out isAir);
                Displace(deltaTime, in movement, isRun, isMoving);

                // 3. Drive animation blend parameters directly from movement intent
                GenAnimationAxis(in movement, isMoving, out animAxis);
            }

            private void ConvertMovement(in Vector2 axis, in Vector3 targetForward, out Vector3 movement)
            {
                Vector3 forward;
                Vector3 right;

                if (m_Space == Space.Self)
                {
                    Vector3 flatForward = new Vector3(targetForward.x, 0f, targetForward.z);
                    if (flatForward.sqrMagnitude > 0.001f)
                    {
                        forward = flatForward.normalized;
                    }
                    else
                    {
                        forward = (m_Transform != null) ? m_Transform.forward : Vector3.forward;
                    }
                    right = Vector3.Cross(Vector3.up, forward).normalized;
                }
                else
                {
                    forward = Vector3.forward;
                    right = Vector3.right;
                }

                movement = axis.x * right + axis.y * forward;

                Vector3 planeNormal = (m_Normal.sqrMagnitude > 0.001f) ? m_Normal : Vector3.up;
                movement = Vector3.ProjectOnPlane(movement, planeNormal);

                if (float.IsNaN(movement.x) || float.IsNaN(movement.y) || float.IsNaN(movement.z))
                {
                    movement = Vector3.zero;
                }
            }

            private void Displace(float deltaTime, in Vector3 movement, bool isRun, bool isMoving)
            {
                if (!isMoving || movement.sqrMagnitude < 0.001f)
                {
                    Vector3 idleDisplacement = m_GravityAcelleration * deltaTime;
                    if (!float.IsNaN(idleDisplacement.x) && !float.IsNaN(idleDisplacement.y) && !float.IsNaN(idleDisplacement.z))
                    {
                        m_Controller.Move(idleDisplacement);
                    }
                    return;
                }

                // Move forward along the character's facing direction projected onto the ground plane
                Vector3 forwardDir = m_Transform != null ? m_Transform.forward : movement.normalized;
                Vector3 planeNormal = (m_Normal.sqrMagnitude > 0.001f) ? m_Normal : Vector3.up;
                Vector3 moveOnPlane = Vector3.ProjectOnPlane(forwardDir, planeNormal).normalized;

                float speed = isRun ? m_RunSpeed : m_WalkSpeed;
                Vector3 displacement = speed * moveOnPlane;
                displacement += m_GravityAcelleration;
                displacement *= deltaTime;

                if (!float.IsNaN(displacement.x) && !float.IsNaN(displacement.y) && !float.IsNaN(displacement.z))
                {
                    m_Controller.Move(displacement);
                }
            }

            private void CaculateGravity(float deltaTime, out bool isAir)
            {
                m_jumpTimer = Mathf.Max(m_jumpTimer - deltaTime, 0f);

                if (m_Controller.isGrounded)
                {
                    m_GravityAcelleration = Physics.gravity;
                    isAir = false;
                    return;
                }

                isAir = true;
                m_GravityAcelleration += Physics.gravity * deltaTime;
            }

            private void GenAnimationAxis(in Vector3 movement, bool isMoving, out Vector2 animAxis)
            {
                if (!isMoving || movement.sqrMagnitude < 0.001f)
                {
                    animAxis = Vector2.zero;
                    return;
                }

                animAxis = new Vector2(0f, 1f);
            }

            private void Turn(in Vector3 targetForward, bool isMoving)
            {
                if (targetForward.sqrMagnitude < 0.001f) return;

                Vector3 flatTarget = Vector3.ProjectOnPlane(targetForward, Vector3.up);
                if (flatTarget.sqrMagnitude < 0.001f) return;

                var angle = Vector3.SignedAngle(m_Transform.forward, flatTarget, Vector3.up);

                if (!m_IsRotating)
                {
                    if (!isMoving && Mathf.Abs(angle) < 5f)
                    {
                        m_IsRotating = false;
                        return;
                    }

                    m_IsRotating = true;
                }

                m_TargetAngle = angle;
            }

            private void UpdateRotation(float deltaTime)
            {
                if (!m_IsRotating)
                {
                    return;
                }

                var rotDelta = m_RotateSpeed * deltaTime;
                if (rotDelta >= Mathf.Abs(m_TargetAngle))
                {
                    m_Transform.Rotate(Vector3.up, m_TargetAngle);
                    m_IsRotating = false;
                }
                else
                {
                    m_Transform.Rotate(Vector3.up, rotDelta * Mathf.Sign(m_TargetAngle));
                }
            }
        }

        private class AnimationHandler
        {
            private readonly Animator m_Animator;
            private readonly string m_VerticalID;
            private readonly string m_StateID;

            private readonly float k_InputFlow = 6f;

            private float m_FlowState;
            private Vector2 m_FlowAxis;

            public AnimationHandler(Animator animator, string verticalID, string stateID)
            {
                m_Animator = animator;
                m_VerticalID = verticalID;
                m_StateID = stateID;
            }

            public void ResetToIdle()
            {
                m_FlowAxis = Vector2.zero;
                m_FlowState = 0f;
                if (m_Animator != null && m_Animator.enabled)
                {
                    m_Animator.SetFloat(m_VerticalID, 0f);
                    m_Animator.SetFloat(m_StateID, 0f);
                }
            }

            public void Animate(in Vector2 axis, float state, float deltaTime)
            {
                if (m_Animator == null || !m_Animator.enabled) return;

                m_FlowAxis = Vector2.MoveTowards(m_FlowAxis, axis, k_InputFlow * deltaTime);
                if (m_FlowAxis.sqrMagnitude < 0.0001f) m_FlowAxis = Vector2.zero;

                m_FlowState = Mathf.MoveTowards(m_FlowState, state, k_InputFlow * deltaTime);
                if (Mathf.Abs(m_FlowState) < 0.001f) m_FlowState = 0f;

                float vert = m_FlowAxis.y;
                m_Animator.SetFloat(m_VerticalID, vert);
                m_Animator.SetFloat(m_StateID, Mathf.Clamp01(m_FlowState));
            }

            public void AnimateIK(in Vector3 target, in LookWeight lookWeight)
            {
                if (m_Animator == null || !m_Animator.enabled) return;
                m_Animator.SetLookAtPosition(target);
                m_Animator.SetLookAtWeight(lookWeight.weight, lookWeight.body, lookWeight.head, lookWeight.eyes);
            }
        }
        #endregion
    }
}