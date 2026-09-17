namespace AiInput.Core;

public static class CompositionPolicy
{
    // A caret may sit at either edge of a composition. Two non-empty selections
    // that merely meet at an edge do not refer to the same pending input.
    public static bool OverlapsFocus(int compositionStartToFocusEnd,int compositionEndToFocusStart,bool collapsedCaret)=>
        collapsedCaret?compositionStartToFocusEnd<=0&&compositionEndToFocusStart>=0:
            compositionStartToFocusEnd<0&&compositionEndToFocusStart>0;
}
