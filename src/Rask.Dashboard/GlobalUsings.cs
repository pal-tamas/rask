// The component TYPES, so a signature can name one. The markup itself needs none of these: a chain
// entry is a member of the markup host, inherited or injected, and is in scope without any using.
global using Rask.Core;
global using Rask.Core.Components;
global using Rask.Html.Components;
// The component kit the console is drawn with, now its own package. The chain entries reach the pages
// by injection like any other; this is here so a signature or a switch can name UiIconName and UiTone.
global using Rask.Ui;
