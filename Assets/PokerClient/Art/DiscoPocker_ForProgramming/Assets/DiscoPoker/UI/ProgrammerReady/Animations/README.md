# Animations

## Disco ball

`Balls/Frames` contains the ten transparent PNG frames from `discoanimation`, preserved in
their original order from `disco-ball-rotation-01.png` through
`disco-ball-rotation-10.png`. Every frame is 1254 x 1254 pixels.

Use the frames as the Sprite sequence of a `UnityEngine.UI.Image`. The matching order is also
recorded in `Balls/animation-frames.json` for tooling and programmer handoff.

No `.anim`, Animator Controller or playback script is included because frame rate, looping and
playback ownership belong to the final screen behaviour. Keeping those choices out of the art
package avoids silently imposing timing on the game.
