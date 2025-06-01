// TalkingStateBehaviour.cs として新規作成
using UnityEngine;

public class TalkingStateBehaviour : StateMachineBehaviour
{
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // Face Layerが会話状態になったら、Body Layerも確実に会話状態にする
        if (layerIndex == 1) // Face Layer
        {
            animator.SetBool("IsTalking", true);
            animator.SetInteger("BodyAnimation", 1); // BODY_TALKING
        }
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 必要に応じて終了時の処理
    }
}