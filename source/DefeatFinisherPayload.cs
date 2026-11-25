using UnityEngine;

// The standalone compiler publicizes these exact existing fields in its private
// reference assembly. The plugin emits ordinary field reads in the real game.
internal static class DefeatFinisherPayload
{
    internal static RectTransform ReadStatusBar(BattleCharacterUI character)
    {
        return character.m_StatusBar;
    }

    internal static int ReadTurn(BattleExecuter executor)
    {
        return executor.m_CurrentHuiHeCount;
    }
}
