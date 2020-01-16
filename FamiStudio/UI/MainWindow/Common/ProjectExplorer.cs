﻿using System;
using System.Drawing;
using System.Media;
using System.Windows.Forms;
using System.Collections.Generic;
using FamiStudio.Properties;
using Color = System.Drawing.Color;

#if FAMISTUDIO_WINDOWS
    using RenderBitmap   = SharpDX.Direct2D1.Bitmap;
    using RenderFont     = SharpDX.DirectWrite.TextFormat;
    using RenderControl  = FamiStudio.Direct2DControl;
    using RenderGraphics = FamiStudio.Direct2DGraphics;
    using RenderTheme    = FamiStudio.Direct2DTheme;
#else
    using RenderBitmap   = FamiStudio.GLBitmap;
    using RenderFont     = FamiStudio.GLFont;
    using RenderControl  = FamiStudio.GLControl;
    using RenderGraphics = FamiStudio.GLGraphics;
    using RenderTheme    = FamiStudio.GLTheme;
#endif

namespace FamiStudio
{
    public class ProjectExplorer : RenderControl
    {
        const int DefaultExpansionTypeSizeX   = 4;
        const int DefaultButtonIconPosX       = 4;
        const int DefaultButtonIconPosY       = 4;
        const int DefaultButtonTextPosX       = 24;
        const int DefaultButtonTextPosY       = 5;
        const int DefaultButtonTextNoIconPosX = 5;
        const int DefaultSubButtonSpacingX    = 20;
        const int DefaultSubButtonPosY        = 4;
        const int DefaultScrollBarSizeX       = 8;
        const int DefaultButtonSizeY          = 23;

        int expansionTypeSizeX;
        int buttonIconPosX;
        int buttonIconPosY;
        int buttonTextPosX;
        int buttonTextPosY;
        int buttonTextNoIconPosX;
        int subButtonSpacingX;
        int subButtonPosY;
        int buttonSizeY;
        int virtualSizeY;
        int scrollBarSizeX;
        bool needsScrollBar;

        enum ButtonType
        {
            ProjectSettings,
            SongHeader,
            Song,
            InstrumentHeader,
            Instrument,
            Max
        };

        enum SubButtonType
        {
            Add,
            DPCM,
            DutyCycle,
            Arpeggio,
            Pitch,
            Volume,
            LoadInstrument,
            Max
        }

        class Button
        {
            public ButtonType type;
            public Song song;
            public Instrument instrument;
            public ProjectExplorer projectExplorer;

            public Button(ProjectExplorer pe)
            {
                projectExplorer = pe;
            }

            public SubButtonType[] GetSubButtons(out bool[] active)
            {
                switch (type)
                {
                    case ButtonType.SongHeader:
                        active = new[] { true };
                        return new[] { SubButtonType.Add };
                    case ButtonType.InstrumentHeader:
                        active = new[] { true ,true };
                        return new[] { SubButtonType.Add ,
                                       SubButtonType.LoadInstrument };
                    case ButtonType.Instrument:
                        if (instrument == null)
                        {
                            active = new[] { true };
                            return new[] { SubButtonType.DPCM };
                        }
                        else
                        {
                            active = new[] {
                                true,
                                instrument.Envelopes[Envelope.Arpeggio].IsEmpty ? false : true,
                                instrument.Envelopes[Envelope.Pitch].IsEmpty ? false : true,
                                instrument.Envelopes[Envelope.Volume].IsEmpty ? false : true
                            };
                            return new[] { SubButtonType.DutyCycle, SubButtonType.Arpeggio, SubButtonType.Pitch, SubButtonType.Volume };
                        }
                }

                active = null;
                return null;
            }

            public string GetText(Project project)
            {
                switch (type)
                {
                    case ButtonType.ProjectSettings: return $"{project.Name} ({project.Author})";
                    case ButtonType.SongHeader: return "Songs";
                    case ButtonType.Song: return song.Name;
                    case ButtonType.InstrumentHeader: return "Instruments";
                    case ButtonType.Instrument: return instrument == null ? "DPCM Samples" : instrument.Name;
                }

                return "";
            }

            public Color GetColor()
            {
                switch (type)
                {
                    case ButtonType.SongHeader:
                    case ButtonType.InstrumentHeader: return ThemeBase.LightGreyFillColor2;
                    case ButtonType.Song: return song.Color;
                    case ButtonType.Instrument: return instrument == null ? ThemeBase.LightGreyFillColor1 : instrument.Color;
                }

                return ThemeBase.LightGreyFillColor1;
            }

            public RenderFont GetFont(Song selectedSong, Instrument selectedInstrument)
            {
                if (type == ButtonType.ProjectSettings)
                {
                    return ThemeBase.FontMediumBoldCenterEllipsis;
                }
                if (type == ButtonType.SongHeader || type == ButtonType.InstrumentHeader)
                {
                    return ThemeBase.FontMediumBoldCenter;
                }
                else if ((type == ButtonType.Song && song == selectedSong) ||
                         (type == ButtonType.Instrument && instrument == selectedInstrument))
                {
                    return ThemeBase.FontMediumBold;
                }
                else
                {
                    return ThemeBase.FontMedium;
                }
            }

            public RenderBitmap GetIcon()
            {
                if (type == ButtonType.Song)
                    return projectExplorer.bmpSong;
                else if (type == ButtonType.Instrument)
                {
                    var expType = instrument != null ? instrument.ExpansionType : Project.ExpansionNone;
                    return projectExplorer.bmpInstrument[expType];
                }
                return null;
            }

            public RenderBitmap GetIcon(SubButtonType sub)
            {
                switch (sub)
                {
                    case SubButtonType.Add:
                        return projectExplorer.bmpAdd;
                    case SubButtonType.DPCM:
                        return projectExplorer.bmpDPCM;
                    case SubButtonType.DutyCycle:
                        if (instrument.ExpansionType == Project.ExpansionVRC6)
                            return projectExplorer.bmpDuty[instrument.DutyCycle];
                        else
                            return projectExplorer.bmpDuty[instrument.DutyCycle * 2];
                    case SubButtonType.Arpeggio:
                        return projectExplorer.bmpArpeggio;
                    case SubButtonType.Pitch:
                        return projectExplorer.bmpPitch;
                    case SubButtonType.Volume:
                        return projectExplorer.bmpVolume;
                    case SubButtonType.LoadInstrument:
                        return projectExplorer.bmpLoadInstrument;
                }

                return null;
            }
        }

        int scrollY = 0;
        int mouseLastY = 0;
        int mouseDragY = -1;
        int envelopeDragIdx = -1;
        bool isDraggingInstrument = false;
        Instrument instrumentDrag = null;
        Song selectedSong = null;
        Instrument selectedInstrument = null; // null = DPCM
        List<Button> buttons = new List<Button>();

        RenderTheme theme;

        RenderBitmap   bmpSong;
        RenderBitmap[] bmpInstrument = new RenderBitmap[Project.ExpansionCount];
        RenderBitmap   bmpAdd;
        RenderBitmap   bmpDPCM;
        RenderBitmap   bmpArpeggio;
        RenderBitmap   bmpPitch;
        RenderBitmap   bmpVolume;
        RenderBitmap   bmpLoadInstrument;
        RenderBitmap[] bmpDuty = new RenderBitmap[8];

        public Song SelectedSong => selectedSong;
        public Instrument SelectedInstrument => selectedInstrument;

        public delegate void EmptyDelegate();
        public delegate void InstrumentEnvelopeDelegate(Instrument instrument, int envelope);
        public delegate void InstrumentDelegate(Instrument instrument);
        public delegate void InstrumentPointDelegate(Instrument instrument, Point pos);
        public delegate void SongDelegate(Song song);

        public event InstrumentEnvelopeDelegate InstrumentEdited;
        public event InstrumentDelegate InstrumentSelected;
        public event InstrumentDelegate InstrumentColorChanged;
        public event InstrumentDelegate InstrumentReplaced;
        public event InstrumentDelegate InstrumentDeleted;
        public event InstrumentPointDelegate InstrumentDraggedOutside;
        public event SongDelegate SongModified;
        public event SongDelegate SongSelected;
        public event EmptyDelegate ExpansionAudioChanged;

        public ProjectExplorer()
        {
            UpdateRenderCoords();
        }

        private void UpdateRenderCoords()
        {
            var scaling = RenderTheme.MainWindowScaling;

            expansionTypeSizeX   = (int)(DefaultExpansionTypeSizeX * scaling);
            buttonIconPosX       = (int)(DefaultButtonIconPosX * scaling);      
            buttonIconPosY       = (int)(DefaultButtonIconPosY * scaling);      
            buttonTextPosX       = (int)(DefaultButtonTextPosX * scaling);      
            buttonTextPosY       = (int)(DefaultButtonTextPosY * scaling);
            buttonTextNoIconPosX = (int)(DefaultButtonTextNoIconPosX * scaling);
            subButtonSpacingX    = (int)(DefaultSubButtonSpacingX * scaling);   
            subButtonPosY        = (int)(DefaultSubButtonPosY * scaling);       
            buttonSizeY          = (int)(DefaultButtonSizeY * scaling);
            virtualSizeY         = App?.Project == null ? Height : buttons.Count * buttonSizeY;
            needsScrollBar       = virtualSizeY > Height; 
            scrollBarSizeX       = needsScrollBar ? (int)(DefaultScrollBarSizeX * scaling) : 0;      
        }

        public void Reset()
        {
            selectedSong = App.Project.Songs[0];
            selectedInstrument = App.Project.Instruments.Count > 0 ? App.Project.Instruments[0] : null;
            RefreshButtons();
            ConditionalInvalidate();
        }

        public void RefreshButtons()
        {
            buttons.Clear();
            buttons.Add(new Button(this) { type = ButtonType.ProjectSettings });
            buttons.Add(new Button(this) { type = ButtonType.SongHeader });

            foreach (var song in App.Project.Songs)
                buttons.Add(new Button(this) { type = ButtonType.Song, song = song });

            buttons.Add(new Button(this) { type = ButtonType.InstrumentHeader });
            buttons.Add(new Button(this) { type = ButtonType.Instrument }); // null instrument = DPCM

            foreach (var instrument in App.Project.Instruments)
                buttons.Add(new Button(this) { type = ButtonType.Instrument, instrument = instrument });

            UpdateRenderCoords();
        }

        protected override void OnRenderInitialized(RenderGraphics g)
        {
            theme = RenderTheme.CreateResourcesForGraphics(g);

            bmpSong = g.CreateBitmapFromResource("Music");
            bmpInstrument[Project.ExpansionNone] = g.CreateBitmapFromResource("Instrument");
            bmpInstrument[Project.ExpansionVRC6] = g.CreateBitmapFromResource("InstrumentVRC6");
            bmpAdd = g.CreateBitmapFromResource("Add");
            bmpDPCM = g.CreateBitmapFromResource("DPCM");
            bmpDuty[0] = g.CreateBitmapFromResource("Duty0");
            bmpDuty[1] = g.CreateBitmapFromResource("Duty1");
            bmpDuty[2] = g.CreateBitmapFromResource("Duty2");
            bmpDuty[3] = g.CreateBitmapFromResource("Duty3");
            bmpDuty[4] = g.CreateBitmapFromResource("Duty4");
            bmpDuty[5] = g.CreateBitmapFromResource("Duty5");
            bmpDuty[6] = g.CreateBitmapFromResource("Duty6");
            bmpDuty[7] = g.CreateBitmapFromResource("Duty7");
            bmpArpeggio = g.CreateBitmapFromResource("Arpeggio");
            bmpPitch = g.CreateBitmapFromResource("Pitch");
            bmpVolume = g.CreateBitmapFromResource("Volume");
            bmpLoadInstrument = g.CreateBitmapFromResource("InstrumentOpen");
        }

        public void ConditionalInvalidate()
        {
            Invalidate();
        }

        protected override void OnRender(RenderGraphics g)
        {

            g.Clear(ThemeBase.DarkGreyFillColor1);
            g.DrawLine(0, 0, 0, Height, theme.BlackBrush);

            int actualWidth = Width - scrollBarSizeX;

            int y = -scrollY;

            foreach (var button in buttons)
            {
                var icon = button.GetIcon();

                g.PushTranslation(0, y);
                g.FillAndDrawRectangle(0, 0, actualWidth, buttonSizeY, g.GetVerticalGradientBrush(button.GetColor(), buttonSizeY, 0.8f), theme.BlackBrush);

                var leftPadding = 0;
                if (App.Project.UsesExpansionAudio && (button.type == ButtonType.Instrument || button.type == ButtonType.Song))
                {
                    var tabColor = button.instrument != null && button.instrument.IsExpansionInstrument ? ThemeBase.DarkGreyFillColor2 : ThemeBase.LightGreyFillColor1;
                    g.FillRectangle(1, 1, 1 + expansionTypeSizeX, buttonSizeY, g.GetVerticalGradientBrush(tabColor, buttonSizeY, 0.8f));
                    g.PushTranslation(1 + expansionTypeSizeX, 0);
                    g.DrawLine(0, 0, 0, buttonSizeY, theme.BlackBrush);
                    leftPadding = expansionTypeSizeX;
                }

                g.DrawText(button.GetText(App.Project), button.GetFont(selectedSong, selectedInstrument), icon == null ? buttonTextNoIconPosX : buttonTextPosX, buttonTextPosY, theme.BlackBrush, actualWidth - buttonTextNoIconPosX * 2);

                if (icon != null)
                {
                    g.DrawBitmap(icon, buttonIconPosX, buttonIconPosY);
                }

                var subButtons = button.GetSubButtons(out var active);
                if (subButtons != null)
                {
                    for (int i = 0, x = actualWidth - subButtonSpacingX - leftPadding; i < subButtons.Length; i++, x -= subButtonSpacingX)
                    {
                        g.DrawBitmap(button.GetIcon(subButtons[i]), x, subButtonPosY, active[i] ? 1.0f : 0.2f);
                    }
                }

                if (leftPadding != 0)
                    g.PopTransform();

                g.PopTransform();
                y += buttonSizeY;
            }

            if (needsScrollBar)
            {
                int virtualSizeY   = this.virtualSizeY;
                int scrollBarSizeY = (int)Math.Round(Height * (Height  / (float)virtualSizeY));
                int scrollBarPosY  = (int)Math.Round(Height * (scrollY / (float)virtualSizeY));

                g.FillAndDrawRectangle(actualWidth, 0, Width - 1, Height, theme.DarkGreyFillBrush1, theme.BlackBrush);
                g.FillAndDrawRectangle(actualWidth, scrollBarPosY, Width - 1, scrollBarPosY + scrollBarSizeY, theme.LightGreyFillBrush1, theme.BlackBrush);
            }
        }

        private void ClampScroll()
        {
            int minScrollY = 0;
            int maxScrollY = Math.Max(virtualSizeY - Height, 0);

            if (scrollY < minScrollY) scrollY = minScrollY;
            if (scrollY > maxScrollY) scrollY = maxScrollY;
        }

        private void DoScroll(int deltaY)
        {
            scrollY -= deltaY;
            ClampScroll();
            ConditionalInvalidate();
        }

        protected void UpdateCursor()
        {
            if (isDraggingInstrument)
            {
#if !FAMISTUDIO_LINUX
                // TODO LINUX: Cursors
                Cursor.Current = envelopeDragIdx == -1 ? Cursors.DragCursor : Cursors.CopyCursor;
#endif
            }
            else
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private int GetButtonAtCoord(int x, int y, out SubButtonType sub)
        {
            var buttonIndex = (y + scrollY) / buttonSizeY;
            sub = SubButtonType.Max;

            if (buttonIndex >= 0 && buttonIndex < buttons.Count)
            {
                var subButtons = buttons[buttonIndex].GetSubButtons(out _);
                if (subButtons != null)
                {
                    y -= (buttonIndex * buttonSizeY - scrollY);

                    for (int i = 0; i < subButtons.Length; i++)
                    {
                        int sx = Width - scrollBarSizeX - subButtonSpacingX * (i + 1);
                        int sy = subButtonPosY;
                        int dx = x - sx;
                        int dy = y - sy;

                        if (dx >= 0 && dx < 16 * RenderTheme.MainWindowScaling &&
                            dy >= 0 && dy < 16 * RenderTheme.MainWindowScaling)
                        {
                            sub = subButtons[i];
                            break;
                        }
                        
                    }
                }

                return buttonIndex;
            }
            else
            {
                return -1;
            }
        }

        private void UpdateToolTip(MouseEventArgs e)
        {
            var tooltip = "";
            var buttonIdx = GetButtonAtCoord(e.X, e.Y, out var subButtonType);

            if (buttonIdx >= 0)
            {
                var buttonType = buttons[buttonIdx].type;

                if (buttonType == ButtonType.SongHeader && subButtonType == SubButtonType.Add)
                {
                    tooltip = "{MouseLeft} Add new song";
                }
                else if (buttonType == ButtonType.Song)
                {
                    tooltip = "{MouseLeft} Make song current - {MouseLeft}{MouseLeft} Song properties - {MouseRight} Delete song";
                }
                else if (buttonType == ButtonType.InstrumentHeader && subButtonType == SubButtonType.Add)
                {
                    tooltip = "{MouseLeft} Add new instrument";
                }
                else if (buttonType == ButtonType.ProjectSettings)
                {
                    tooltip = "{MouseLeft}{MouseLeft} Project properties";
                }
                else if (buttonType == ButtonType.Instrument)
                {
                    if (subButtonType == SubButtonType.Max)
                    {
                        if (buttons[buttonIdx].instrument == null)
                            tooltip = "{MouseLeft} Select instrument";
                        else
                            tooltip = "{MouseLeft} Select instrument - {MouseLeft}{MouseLeft} Instrument properties - {MouseRight} Delete instrument - {Drag} Replace instrument";
                    }
                    else
                    {
                        if (subButtonType == SubButtonType.Volume)
                            tooltip = "{MouseLeft} Edit volume enveloppe - {MouseRight} Delete envelope - {Drag} Copy envelope";
                        else if (subButtonType == SubButtonType.Pitch)
                            tooltip = "{MouseLeft} Edit pitch enveloppe - {MouseRight} Delete envelope - {Drag} Copy envelope";
                        else if (subButtonType == SubButtonType.Arpeggio)
                            tooltip = "{MouseLeft} Edit arpeggio enveloppe - {MouseRight} Delete envelope - {Drag} Copy envelope";
                        else if (subButtonType == SubButtonType.DPCM)
                            tooltip = "{MouseLeft} Edit DPCM samples";
                        else if (subButtonType == SubButtonType.DutyCycle)
                            tooltip = "{MouseLeft} Change duty cycle";
                    }
                }
            }

            App.ToolTip = tooltip;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool left   = e.Button.HasFlag(MouseButtons.Left);
            bool middle = e.Button.HasFlag(MouseButtons.Middle) || (e.Button.HasFlag(MouseButtons.Left) && ModifierKeys.HasFlag(Keys.Alt));

            UpdateToolTip(e);

            if (left && mouseDragY > 0 && !isDraggingInstrument && Math.Abs(e.Y - mouseDragY) > 5)
            {
                isDraggingInstrument = true;
                Capture = true;
            }
            if (middle)
            {
                int deltaY = e.Y - mouseLastY;
                DoScroll(deltaY);
                mouseLastY = e.Y;
            }

            UpdateCursor();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (isDraggingInstrument)
            {
                if (ClientRectangle.Contains(e.X, e.Y))
                {
                    var buttonIdx = GetButtonAtCoord(e.X, e.Y, out var subButtonType);

                    var instrumentSrc = instrumentDrag;
                    var instrumentDst = buttonIdx >= 0 && buttons[buttonIdx].type == ButtonType.Instrument ? buttons[buttonIdx].instrument : null;

                    if (instrumentSrc != instrumentDst && instrumentSrc != null && instrumentDst != null && instrumentSrc.ExpansionType == instrumentDst.ExpansionType)
                    {
                        if (envelopeDragIdx == -1)
                        {
                            if (PlatformUtils.MessageBox($"Are you sure you want to replace all notes of instrument '{instrumentDst.Name}' with '{instrumentSrc.Name}'?", "Replace intrument", MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                                App.Project.ReplaceInstrument(instrumentDst, instrumentSrc);
                                App.UndoRedoManager.EndTransaction();

                                InstrumentReplaced?.Invoke(instrumentDst);
                            }
                        }
                        else
                        {
                            if (PlatformUtils.MessageBox($"Are you sure you want to copy the {Envelope.EnvelopeStrings[envelopeDragIdx]} envelope of instrument '{instrumentSrc.Name}' to '{instrumentDst.Name}'?", "Copy Envelope", MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                App.UndoRedoManager.BeginTransaction(TransactionScope.Instrument, instrumentDst.Id);
                                instrumentDst.Envelopes[envelopeDragIdx] = instrumentSrc.Envelopes[envelopeDragIdx].Clone();
                                App.UndoRedoManager.EndTransaction();

                                InstrumentEdited?.Invoke(instrumentDst, envelopeDragIdx);
                                Invalidate();
                            }
                        }
                    }
                }
                else
                {
                    InstrumentDraggedOutside(instrumentDrag, PointToScreen(new Point(e.X, e.Y)));
                }

                Capture = false;
            }

            CancelDrag();
        }

        protected void CancelDrag()
        {
            isDraggingInstrument = false;
            mouseDragY = -1;
            instrumentDrag = null;
            envelopeDragIdx = -1;
            UpdateCursor();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            DoScroll(e.Delta > 0 ? buttonSizeY * 3 : -buttonSizeY * 3);
        }

        protected override void OnResize(EventArgs e)
        {
            UpdateRenderCoords();
            ClampScroll();
            base.OnResize(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            bool left   = e.Button.HasFlag(MouseButtons.Left);
            bool middle = e.Button.HasFlag(MouseButtons.Middle) || (e.Button.HasFlag(MouseButtons.Left) && ModifierKeys.HasFlag(Keys.Alt));
            bool right  = e.Button.HasFlag(MouseButtons.Right);

            var buttonIdx = GetButtonAtCoord(e.X, e.Y, out var subButtonType);

            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];

                if (left)
                {
                    if (button.type == ButtonType.SongHeader)
                    {
                        if (subButtonType == SubButtonType.Add)
                        {
                            App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                            App.Project.CreateSong();
                            App.UndoRedoManager.EndTransaction();
                            RefreshButtons();
                            ConditionalInvalidate();
                        }
                    }
                    else if (button.type == ButtonType.Song)
                    {
                        if (button.song != selectedSong)
                        {
                            selectedSong = button.song;
                            SongSelected?.Invoke(selectedSong);
                            ConditionalInvalidate();
                        }
                    }
                    else if (button.type == ButtonType.InstrumentHeader)
                    {
                        if (subButtonType == SubButtonType.Add)
                        {
                            var instrumentType = Project.ExpansionNone;

                            if (App.Project.UsesExpansionAudio)
                            {
                                var dlg = new PropertyDialog(PointToScreen(new Point(e.X, e.Y)), 160, true);
                                dlg.Properties.AddStringList("Expansion:", Project.ExpansionNames, Project.ExpansionNames[0] ); // 0
                                dlg.Properties.Build();

                                if (dlg.ShowDialog() == DialogResult.OK)
                                    instrumentType = Array.IndexOf(Project.ExpansionNames, dlg.Properties.GetPropertyValue<string>(0));
                                else
                                    return;
                            }

                            App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                            App.Project.CreateInstrument(instrumentType);
                            App.UndoRedoManager.EndTransaction();
                            RefreshButtons();
                            ConditionalInvalidate();
                        }
                        if (subButtonType == SubButtonType.LoadInstrument)
                        {
                            var filename = PlatformUtils.ShowOpenFileDialog("Open File", "Fami Tracker Instrument (*.fti)|*.fti");
                            if (filename != null)
                            {
                                App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                                var instrument = FamitrackerInstrumentFile.CreateFromFile(App.Project, filename);
                                if (instrument == null)
                                    App.UndoRedoManager.AbortTransaction();
                                else
                                    App.UndoRedoManager.EndTransaction();
                            }

                            RefreshButtons();
                            ConditionalInvalidate();
                        }
                    }
                    else if (button.type == ButtonType.Instrument)
                    {
                        selectedInstrument = button.instrument;

                        if (selectedInstrument != null)
                        {
                            instrumentDrag = selectedInstrument;
                            mouseDragY = e.Y;
                        }
                    
                        if (subButtonType == SubButtonType.Volume)
                        {
                            InstrumentEdited?.Invoke(selectedInstrument, Envelope.Volume);
                            envelopeDragIdx = Envelope.Volume;
                        }
                        else if (subButtonType == SubButtonType.Pitch)
                        {
                            InstrumentEdited?.Invoke(selectedInstrument, Envelope.Pitch);
                            envelopeDragIdx = Envelope.Pitch;
                        }
                        else if (subButtonType == SubButtonType.Arpeggio)
                        {
                            InstrumentEdited?.Invoke(selectedInstrument, Envelope.Arpeggio);
                            envelopeDragIdx = Envelope.Arpeggio;
                        }
                        else if (subButtonType == SubButtonType.DPCM)
                        {
                            InstrumentEdited?.Invoke(selectedInstrument, Envelope.Max);
                        }
                        else if (subButtonType == SubButtonType.DutyCycle)
                        {
                            selectedInstrument.DutyCycle = (selectedInstrument.DutyCycle + 1) % selectedInstrument.DutyCycleRange;
                        }

                        InstrumentSelected?.Invoke(selectedInstrument);
                        ConditionalInvalidate();
                    }
                }
                else if (right)
                {
                    if (button.type == ButtonType.Song && App.Project.Songs.Count > 1)
                    {
                        var song = button.song;
                        if (PlatformUtils.MessageBox($"Are you sure you want to delete '{song.Name}' ?", "Delete song", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            bool selectNewSong = song == selectedSong;
                            App.Stop();
                            App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                            App.Project.DeleteSong(song);
                            if (selectNewSong)
                                selectedSong = App.Project.Songs[0];
                            SongSelected?.Invoke(selectedSong);
                            App.UndoRedoManager.EndTransaction();
                            RefreshButtons();
                            ConditionalInvalidate();
                        }
                    }
                    else if (button.type == ButtonType.Instrument && button.instrument != null)
                    {
                        var instrument = button.instrument;

                        if (subButtonType == SubButtonType.Arpeggio ||
                            subButtonType == SubButtonType.Pitch ||
                            subButtonType == SubButtonType.Volume)
                        {
                            int envType = Envelope.Volume;

                            switch (subButtonType)
                            {
                                case SubButtonType.Arpeggio: envType = Envelope.Arpeggio; break;
                                case SubButtonType.Pitch: envType = Envelope.Pitch;    break;
                            }

                            App.UndoRedoManager.BeginTransaction(TransactionScope.Instrument, instrument.Id);
                            instrument.Envelopes[envType].Length = 0;
                            App.UndoRedoManager.EndTransaction();
                            ConditionalInvalidate();
                        }
                        else if (subButtonType == SubButtonType.Max)
                        {
                            if (PlatformUtils.MessageBox($"Are you sure you want to delete '{instrument.Name}' ? All notes using this instrument will be deleted.", "Delete intrument", MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                bool selectNewInstrument = instrument == selectedInstrument;
                                App.StopInstrumentNoteAndWait();
                                App.UndoRedoManager.BeginTransaction(TransactionScope.Project);
                                App.Project.DeleteInstrument(instrument);
                                if (selectNewInstrument)
                                    selectedInstrument = App.Project.Instruments.Count > 0 ? App.Project.Instruments[0] : null;
                                SongSelected?.Invoke(selectedSong);
                                InstrumentDeleted?.Invoke(instrument);
                                App.UndoRedoManager.EndTransaction();
                                RefreshButtons();
                                ConditionalInvalidate();
                            }
                        }
                    }
                }
            }

            if (middle)
            {
                mouseLastY = e.Y;
            }
        }

        private int[] GenerateBarLengths(int patternLen)
        {
            var barLengths = Song.GenerateBarLengths(patternLen);

#if !FAMISTUDIO_WINDOWS
            Array.Reverse(barLengths);
#endif

            return barLengths;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            var buttonIdx = GetButtonAtCoord(e.X, e.Y, out var subButtonType);

            if (buttonIdx >= 0)
            {
                var button = buttons[buttonIdx];

                if (button.type == ButtonType.ProjectSettings)
                {
                    var project = App.Project;

                    var dlg = new PropertyDialog(PointToScreen(new Point(e.X, e.Y)), 250, true);
                    dlg.Properties.AddString("Title :", project.Name, 31); // 0
                    dlg.Properties.AddString("Author :", project.Author, 31); // 1
                    dlg.Properties.AddString("Copyright :", project.Copyright, 31); // 2
                    dlg.Properties.AddStringList("Expansion Audio:", Project.ExpansionNames, project.ExpansionAudioName); // 3
                    dlg.Properties.Build();

                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        App.UndoRedoManager.BeginTransaction(TransactionScope.Project);

                        project.Name = dlg.Properties.GetPropertyValue<string>(0);
                        project.Author = dlg.Properties.GetPropertyValue<string>(1);
                        project.Copyright = dlg.Properties.GetPropertyValue<string>(2);

                        var expansion = Array.IndexOf(Project.ExpansionNames, dlg.Properties.GetPropertyValue<string>(3));
                        if (expansion != project.ExpansionAudio)
                        {
                            if (project.ExpansionAudio == Project.ExpansionNone ||
                                PlatformUtils.MessageBox($"Switching expansion audio will delete all instruments and channels using the old expansion?", "Change expansion audio", MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                App.StopInstrumentPlayer();
                                project.SetExpansionAudio(expansion);
                                ExpansionAudioChanged?.Invoke();
                                App.StartInstrumentPlayer();
                                RefreshButtons();
                            }
                        }

                        App.UndoRedoManager.EndTransaction();
                        ConditionalInvalidate();
                    }
                }
                else if (button.type == ButtonType.Song)
                {
                    var song = button.song;

                    var dlg = new PropertyDialog(PointToScreen(new Point(e.X, e.Y)), 200, true);
                    dlg.Properties.AddColoredString(song.Name, song.Color); // 0
                    dlg.Properties.AddIntegerRange("Tempo :", song.Tempo, 32, 255); // 1
                    dlg.Properties.AddIntegerRange("Speed :", song.Speed, 1, 31); // 2
                    dlg.Properties.AddIntegerRange("Pattern Length :", song.PatternLength, 16, 256); // 3
                    dlg.Properties.AddDomainRange("Bar Length :", GenerateBarLengths(song.PatternLength), song.BarLength); // 4
                    dlg.Properties.AddIntegerRange("Song Length :", song.Length, 1, 128); // 5
                    dlg.Properties.AddColor(song.Color); // 6
                    dlg.Properties.Build();
                    dlg.Properties.PropertyChanged += Properties_PropertyChanged;

                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        App.UndoRedoManager.BeginTransaction(TransactionScope.Project);

                        App.Stop();
                        App.Seek(0);

                        var newName = dlg.Properties.GetPropertyValue<string>(0);

                        if (App.Project.RenameSong(song, newName))
                        {
                            song.Color = dlg.Properties.GetPropertyValue<System.Drawing.Color>(6);
                            song.Tempo = dlg.Properties.GetPropertyValue<int>(1);
                            song.Speed = dlg.Properties.GetPropertyValue<int>(2);
                            song.SetLength(dlg.Properties.GetPropertyValue<int>(5));
                            song.SetPatternLength(dlg.Properties.GetPropertyValue<int>(3));
                            song.SetBarLength(dlg.Properties.GetPropertyValue<int>(4));
                            SongModified?.Invoke(song);
                            App.UndoRedoManager.EndTransaction();
                            RefreshButtons();
                        }
                        else
                        {
                            App.UndoRedoManager.AbortTransaction();
                            SystemSounds.Beep.Play();
                        }

                        ConditionalInvalidate();
                    }
                }
                else if (button.type == ButtonType.Instrument && button.instrument != null)
                {
                    var instrument = button.instrument;

                    if (subButtonType == SubButtonType.Max)
                    {
                        var dlg = new PropertyDialog(PointToScreen(new Point(e.X, e.Y)), 160, true);
                        dlg.Properties.AddColoredString(instrument.Name, instrument.Color); // 0
                        dlg.Properties.AddColor(instrument.Color); // 1
                        dlg.Properties.AddBoolean("Relative pitch:", instrument.Envelopes[Envelope.Pitch].Relative); // 2
                        dlg.Properties.Build();

                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            var newName  = dlg.Properties.GetPropertyValue<string>(0);

                            App.UndoRedoManager.BeginTransaction(TransactionScope.Project);

                            if (App.Project.RenameInstrument(instrument, newName))
                            {
                                instrument.Color = dlg.Properties.GetPropertyValue<System.Drawing.Color>(1);
                                instrument.Envelopes[Envelope.Pitch].Relative = dlg.Properties.GetPropertyValue<bool>(2);
                                InstrumentColorChanged?.Invoke(instrument);
                                RefreshButtons();
                                ConditionalInvalidate();
                                App.UndoRedoManager.EndTransaction();
                            }
                            else
                            {
                                App.UndoRedoManager.AbortTransaction();
                                SystemSounds.Beep.Play();
                            }
                        }
                    }
                }
            }
        }

        private void Properties_PropertyChanged(PropertyPage props, int idx, object value)
        {
            if (idx == 3) // 3 = pattern length.
            {
                var barLengths = GenerateBarLengths((int)value);
                var barIdx = Array.IndexOf(barLengths, selectedSong.BarLength);

                if (barIdx == -1)
                    barIdx = barLengths.Length - 1;

                props.UpdateDomainRange(4, barLengths, barLengths[barIdx]); // 4 = bar length.
            }
        }

        public void SerializeState(ProjectBuffer buffer)
        {
            buffer.Serialize(ref selectedSong);
            buffer.Serialize(ref selectedInstrument);
            buffer.Serialize(ref scrollY);

            if (buffer.IsReading)
            {
                CancelDrag();
                ClampScroll();
                RefreshButtons();
                ConditionalInvalidate();
            }
        }
    }
}
