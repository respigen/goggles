# goggles
* `dotnet build Goggles.csproj`
* Run the application, it sits in the tray.
* By default, Ctrl+Windows+F11 toggles transparency.

![A .gif demonstrating Goggles toggling the transparency of a window](README.gif)

## Future
* Windows can support windows you can click through with `WS_EX_TRANSPARENT`, and it can also support always on top. I don't need always on top (PowerToys does that for me) but I would like to have click-through, which might only be useful if they're always on top.
  * I'll need a hotkey for that, to set a window
  * I don't know if I'd be able to make the window my active window again, so maybe I'll need another hotkey to universally remove all `WS_EX_TRANSPARENT` styles.
* Maybe a sound when toggling? The system sounds aren't inspiring. It'd be an excuse to design sounds too.
* I'd like to play with the colorref transparency options.
