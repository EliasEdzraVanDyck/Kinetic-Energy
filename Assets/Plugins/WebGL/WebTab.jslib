mergeInto(LibraryManager.library, {

  // Best-effort tab close for the Quit button. Browsers only let a script close a
  // window that a script opened, and inside itch's iframe the TOP tab is cross-origin -
  // so every route is tried, and where the browser refuses them all, a full-page notice
  // tells the player the game has ended and the tab is theirs to close. Nothing here
  // exists in a Windows build; the .jslib only compiles into WebGL.
  CloseGameTab: function () {
    try { window.top.close(); } catch (e) {}
    try { window.close(); } catch (e) {}
    setTimeout(function () {
      try {
        if (document.exitFullscreen) document.exitFullscreen();
        var notice = document.createElement("div");
        notice.style = "position:fixed;inset:0;z-index:1000;background:#000;color:#fff;" +
          "display:flex;align-items:center;justify-content:center;flex-direction:column;" +
          "font-family:system-ui,sans-serif;text-align:center";
        notice.innerHTML = "<div style='font-size:26px;margin-bottom:8px'>Thanks for playing!</div>" +
          "<div style='font-size:14px;opacity:0.7'>You can close this tab now.</div>";
        (window.top === window ? document.body : document.body).appendChild(notice);
      } catch (e) {}
    }, 150);
  }

});
