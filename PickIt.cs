using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using SharpDX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Input = ExileCore.Input;

namespace PickIt
{
    public class PickIt : BaseSettingsPlugin<PickItSettings>
    {
        private readonly List<Entity> _entities = new List<Entity>();
        private readonly Stopwatch _pickUpTimer = Stopwatch.StartNew();
        private readonly Stopwatch DebugTimer = Stopwatch.StartNew();

        private Vector2 _clickWindowOffset;
        private WaitTime _workCoroutine;
        private uint coroutineCounter;
        private Vector2 cursorBeforePickIt;
        private bool FullWork = true;
        private Element LastLabelClick;
        private Coroutine pickItCoroutine;

        private WaitTime ToPick => new WaitTime(5);
        private WaitTime Wait3ms => new WaitTime(3);
        private WaitTime WaitForNextTry => new WaitTime(5);

        public override bool Initialise()
        {
            pickItCoroutine = new Coroutine(MainWorkCoroutine(), this, "Pick It");
            Core.ParallelRunner.Run(pickItCoroutine);
            pickItCoroutine.Pause();
            DebugTimer.Reset();
            Settings.MouseSpeed.OnValueChanged += (sender, f) => { Mouse.speedMouse = Settings.MouseSpeed.Value; };
            _workCoroutine = new WaitTime(Settings.ExtraDelay);
            Settings.ExtraDelay.OnValueChanged += (sender, i) => _workCoroutine = new WaitTime(i);
            return true;
        }

        private IEnumerator MainWorkCoroutine()
        {
            while (true)
            {
                yield return FindItemToPick();

                coroutineCounter++;
                pickItCoroutine.UpdateTicks(coroutineCounter);
                yield return _workCoroutine;
            }
        }


        public override Job Tick()
        {
            if (Input.GetKeyState(Keys.Escape)) pickItCoroutine.Pause();

            if (Input.GetKeyState(Settings.PickUpKey.Value))
            {
                DebugTimer.Restart();

                if (pickItCoroutine.IsDone)
                {
                    var firstOrDefault = Core.ParallelRunner.Coroutines.FirstOrDefault(x => x.OwnerName == nameof(PickIt));

                    if (firstOrDefault != null)
                        pickItCoroutine = firstOrDefault;
                }

                pickItCoroutine.Resume();
                FullWork = false;
            }
            else
            {
                if (FullWork)
                {
                    pickItCoroutine.Pause();
                    DebugTimer.Reset();
                }
            }

            if (DebugTimer.ElapsedMilliseconds > 2000)
            {
                FullWork = true;
                LogMessage("Error pick it stop after time limit 2000 ms", 1);
                DebugTimer.Reset();
            }

            return null;
        }


        public bool DoWePickThis(CustomItem itemEntity)
        {
            if (!itemEntity.IsValid)
                return false;

            return true; 
        }

        private IEnumerator FindItemToPick()
        {
            if (!Input.GetKeyState(Settings.PickUpKey.Value) || !GameController.Window.IsForeground()) yield break;
            var window = GameController.Window.GetWindowRectangleTimeCache;
            var rect = new RectangleF(window.X, window.X, window.X + window.Width, window.Y + window.Height);
            var playerPos = GameController.Player.GridPos;

            List<CustomItem> currentLabels;

            currentLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabels
                .Where(x => x.Address != 0 &&
                            x.ItemOnGround?.Path != null &&
                            x.IsVisible && x.Label.GetClientRectCache.Center.PointInRectangle(rect) &&
                            (x.CanPickUp || x.MaxTimeForPickUp.TotalSeconds <= 0))
                .Select(x => new CustomItem(x, GameController.Files, x.ItemOnGround.DistancePlayer))
                .OrderBy(x => x.Distance).ToList();
            

            GameController.Debug["PickIt"] = currentLabels;
            var pickUpThisItem = currentLabels.FirstOrDefault(x => DoWePickThis(x) && x.Distance < Settings.PickupRange);
            if (pickUpThisItem?.GroundItem != null) yield return TryToPickV2(pickUpThisItem);
            FullWork = true;
        }

        private IEnumerator TryToPickV2(CustomItem pickItItem)
        {
            if (!pickItItem.IsValid)
            {
                FullWork = true;
                LogMessage("PickItem is not valid.", 5, Color.Red);
                yield break;
            }

            var centerOfItemLabel = pickItItem.LabelOnGround.Label.GetClientRectCache.Center;
            var rectangleOfGameWindow = GameController.Window.GetWindowRectangleTimeCache;
            var oldMousePosition = Mouse.GetCursorPositionVector();
            _clickWindowOffset = rectangleOfGameWindow.TopLeft;
            rectangleOfGameWindow.Inflate(-55, -55);
            centerOfItemLabel.X += rectangleOfGameWindow.Left;
            centerOfItemLabel.Y += rectangleOfGameWindow.Top;

            if (!rectangleOfGameWindow.Intersects(new RectangleF(centerOfItemLabel.X, centerOfItemLabel.Y, 3, 3)))
            {
                FullWork = true;
                //LogMessage($"Label outside game window. Label: {centerOfItemLabel} Window: {rectangleOfGameWindow}", 5, Color.Red);
                yield break;
            }

            var tryCount = 0;

            while (!pickItItem.IsTargeted() && tryCount < 5)
            {
                var completeItemLabel = pickItItem.LabelOnGround?.Label;

                if (completeItemLabel == null)
                {
                    if (tryCount > 0)
                    {
                        LogMessage("Probably item already picked.", 3);
                        yield break;
                    }

                    LogError("Label for item not found.", 5);
                    yield break;
                }

                /*while (GameController.Player.GetComponent<Actor>().isMoving)
                {
                    yield return waitPlayerMove;
                }*/
                var clientRect = completeItemLabel.GetClientRect();

                var clientRectCenter = clientRect.Center;

                var vector2 = clientRectCenter + _clickWindowOffset;

                Mouse.MoveCursorToPosition(vector2);
                yield return Wait3ms;
                Mouse.MoveCursorToPosition(vector2);
                yield return Wait3ms;
                yield return Mouse.LeftClick();
                yield return ToPick;
                tryCount++;
            }

            if (pickItItem.IsTargeted())
                Input.Click(MouseButtons.Left);

            tryCount = 0;

            while (GameController.Game.IngameState.IngameUi.ItemsOnGroundLabels.FirstOrDefault(
                       x => x.Address == pickItItem.LabelOnGround.Address) != null && tryCount < 6)
            {
                tryCount++;
                yield return WaitForNextTry;
            }
        }
    }
}
