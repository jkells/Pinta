/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Olivier Dufour <olivier.duff@gmail.com>                 //
//                     Jonathan Pobst <monkey@jpobst.com>                      //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using Gdk;
using Gtk;
using Mono.Unix;
using Pinta.Core;

namespace Pinta.Tools
{
	[System.ComponentModel.Composition.Export (typeof (BaseTool))]
	public class TextTool : BaseTool
	{
		// Variables for dragging
		private Cairo.PointD startMouseXY;
		private Point startClickPoint;
		private bool tracking;
		private Gdk.Cursor cursor_hand;

		private Point clickPoint;
		private bool is_editing;
		private Rectangle old_bounds = Rectangle.Zero;

		private TextEngine engine;

		public override string Name { get { return Catalog.GetString ("Text"); } }
		public override string Icon { get { return "Tools.Text.png"; } }
		public override Gdk.Key ShortcutKey { get { return Gdk.Key.T; } }
		public override int Priority { get { return 37; } }

		public override string StatusBarText {
			get { return Catalog.GetString ("Left click to place cursor, then type desired text. Text color is primary color."); }
		}

		#region Constructor
		public TextTool ()
		{
			cursor_hand = new Gdk.Cursor (PintaCore.Chrome.DrawingArea.Display, PintaCore.Resources.GetIcon ("Tools.Pan.png"), 0, 0);
			engine = new TextEngine ();
		}
		#endregion

		#region ToolBar
		private ToolBarLabel font_label;
		private ToolBarComboBox font_combo;
		private ToolBarComboBox size_combo;
		private ToolBarToggleButton bold_btn;
		private ToolBarToggleButton italic_btn;
		private ToolBarToggleButton underscore_btn;
		private ToolBarToggleButton left_alignment_btn;
		private ToolBarToggleButton center_alignment_btn;
		private ToolBarToggleButton Right_alignment_btn;
		private ToolBarLabel spacer_label;

		protected void RenderFont (Gtk.CellLayout layout, Gtk.CellRenderer renderer, Gtk.TreeModel model, Gtk.TreeIter iter)
		{
			string fontName = (string)model.GetValue (iter, 0);
			Gtk.CellRendererText cell = renderer as Gtk.CellRendererText;
			cell.Text = fontName;
			cell.Font = string.Format ("{0} 10", fontName);
			cell.Family = fontName;
		}

		protected override void OnBuildToolBar (Gtk.Toolbar tb)
		{
			base.OnBuildToolBar (tb);
			
			if (font_label == null)
				font_label = new ToolBarLabel (string.Format (" {0}: ", Catalog.GetString ("Font")));
			
			tb.AppendItem (font_label);

			if (font_combo == null) {
				var fonts = PintaCore.System.Fonts.GetInstalledFonts ();
				fonts.Sort ();

				// Default to Arial or first in list
				int index = Math.Max (fonts.IndexOf ("Arial"), 0);

				font_combo = new ToolBarComboBox (150, index, false, fonts.ToArray ());
				font_combo.ComboBox.Changed += HandleFontChanged;
				font_combo.ComboBox.SetCellDataFunc (font_combo.CellRendererText, new CellLayoutDataFunc (RenderFont));
			}

			tb.AppendItem (font_combo);

			if (spacer_label == null)
				spacer_label = new ToolBarLabel (" ");

			tb.AppendItem (spacer_label);

			if (size_combo == null) {
				size_combo = new ToolBarComboBox (65, 0, true);

				size_combo.ComboBox.Changed += HandleSizeChanged;
				(size_combo.ComboBox as Gtk.ComboBoxEntry).Entry.FocusOutEvent += new Gtk.FocusOutEventHandler (HandleFontSizeFocusOut);
				(size_combo.ComboBox as Gtk.ComboBoxEntry).Entry.FocusInEvent += new Gtk.FocusInEventHandler (HandleFontSizeFocusIn);
			}

			tb.AppendItem (size_combo);

			tb.AppendItem (new SeparatorToolItem ());
			
			if (bold_btn == null) {
				bold_btn = new ToolBarToggleButton ("Toolbar.Bold.png", Catalog.GetString ("Bold"), Catalog.GetString ("Bold"));
				bold_btn.Toggled += HandleBoldButtonToggled;
			}
			
			tb.AppendItem (bold_btn);
			
			if (italic_btn == null) {
				italic_btn = new ToolBarToggleButton ("Toolbar.Italic.png", Catalog.GetString ("Italic"), Catalog.GetString ("Italic"));
				italic_btn.Toggled += HandleItalicButtonToggled;
			}
			
			tb.AppendItem (italic_btn);
			
			if (underscore_btn == null) {
				underscore_btn = new ToolBarToggleButton ("Toolbar.Underline.png", Catalog.GetString ("Underline"), Catalog.GetString ("Underline"));
				underscore_btn.Toggled += HandleUnderscoreButtonToggled;
			}
			
			tb.AppendItem (underscore_btn);
			
			tb.AppendItem (new SeparatorToolItem ());
			
			if (left_alignment_btn == null) {
				left_alignment_btn = new ToolBarToggleButton ("Toolbar.LeftAlignment.png", Catalog.GetString ("Left Align"), Catalog.GetString ("Left Align"));
				left_alignment_btn.Active = true;
				left_alignment_btn.Toggled += HandleLeftAlignmentButtonToggled;
			}
			
			tb.AppendItem (left_alignment_btn);
			
			if (center_alignment_btn == null) {
				center_alignment_btn = new ToolBarToggleButton ("Toolbar.CenterAlignment.png", Catalog.GetString ("Center Align"), Catalog.GetString ("Center Align"));
				center_alignment_btn.Toggled += HandleCenterAlignmentButtonToggled;
			}
			
			tb.AppendItem (center_alignment_btn);
			
			if (Right_alignment_btn == null) {
				Right_alignment_btn = new ToolBarToggleButton ("Toolbar.RightAlignment.png", Catalog.GetString ("Right Align"), Catalog.GetString ("Right Align"));
				Right_alignment_btn.Toggled += HandleRightAlignmentButtonToggled;
			}
			
			tb.AppendItem (Right_alignment_btn);

			UpdateFontSizes ();
		}

		string temp_size;

		private void HandleFontSizeFocusIn (object o, FocusInEventArgs args)
		{
			size_combo.ComboBox.Changed -= HandleSizeChanged;
			temp_size = size_combo.ComboBox.ActiveText;
		}

		private void HandleFontSizeFocusOut (object o, FocusOutEventArgs args)
		{
			string text = size_combo.ComboBox.ActiveText;
			int size;

			if (!int.TryParse (text, out size)) {
				(size_combo.ComboBox as Gtk.ComboBoxEntry).Entry.Text = temp_size;
				return;
			}
			
			PintaCore.Chrome.DrawingArea.GrabFocus ();

			UpdateFont ();

			size_combo.ComboBox.Changed += HandleSizeChanged;
		}

		private void HandleFontChanged (object sender, EventArgs e)
		{
			PintaCore.Chrome.DrawingArea.GrabFocus ();

			UpdateFontSizes ();
			UpdateFont ();
		}

		private void UpdateFontSizes ()
		{
			string oldval = size_combo.ComboBox.ActiveText;

			ListStore model = (ListStore)size_combo.ComboBox.Model;
			model.Clear ();

			List<int> sizes = PintaCore.System.Fonts.GetSizes (FontFamily);

			foreach (int i in sizes)
				size_combo.ComboBox.AppendText (i.ToString ());
			
			int index;
			
			if (string.IsNullOrEmpty (oldval))
				index = sizes.IndexOf (12);
			else
				index = sizes.IndexOf (int.Parse (oldval));

			if (index == -1)
				index = 0;
			
			size_combo.ComboBox.Active = index;
		}

		private void HandleSizeChanged (object sender, EventArgs e)
		{
			PintaCore.Chrome.DrawingArea.GrabFocus ();

			UpdateFont ();
		}

		private Pango.FontFamily FontFamily {
			get { return PintaCore.System.Fonts.GetFamily (font_combo.ComboBox.ActiveText); }
		}


		private int FontSize {
			get { return int.Parse (size_combo.ComboBox.ActiveText); }
		}

		private TextAlignment Alignment {
			get {
				if (Right_alignment_btn.Active)
					return TextAlignment.Right; 
				else if (center_alignment_btn.Active)
					return TextAlignment.Center;
				else
					return TextAlignment.Left;
			}
		}

		private string Font {
			get { return font_combo.ComboBox.ActiveText; }
		}

		private void HandlePintaCorePalettePrimaryColorChanged (object sender, EventArgs e)
		{
			if (is_editing)
				RedrawText (true, true);
		}

		private void HandleLeftAlignmentButtonToggled (object sender, EventArgs e)
		{
			if (left_alignment_btn.Active) {
				Right_alignment_btn.Active = false;
				center_alignment_btn.Active = false;
			} else if (!Right_alignment_btn.Active && !center_alignment_btn.Active) {
				left_alignment_btn.Active = true;
			}

			UpdateFont ();
		}

		private void HandleCenterAlignmentButtonToggled (object sender, EventArgs e)
		{
			if (center_alignment_btn.Active) {
				Right_alignment_btn.Active = false;
				left_alignment_btn.Active = false;
			} else if (!Right_alignment_btn.Active && !left_alignment_btn.Active) {
				center_alignment_btn.Active = true;
			}

			UpdateFont ();
		}

		private void HandleRightAlignmentButtonToggled (object sender, EventArgs e)
		{
			if (Right_alignment_btn.Active) {
				center_alignment_btn.Active = false;
				left_alignment_btn.Active = false;
			} else if (!center_alignment_btn.Active && !left_alignment_btn.Active) {
				Right_alignment_btn.Active = true;
			}

			UpdateFont ();
		}

		private void HandleUnderscoreButtonToggled (object sender, EventArgs e)
		{
			UpdateFont ();
		}

		private void HandleItalicButtonToggled (object sender, EventArgs e)
		{
			UpdateFont ();
		}

		private void HandleBoldButtonToggled (object sender, EventArgs e)
		{
			UpdateFont ();
		}

		private void UpdateFont ()
		{
			engine.SetAlignment (Alignment);
			engine.SetFont (Font, FontSize, bold_btn.Active, italic_btn.Active, underscore_btn.Active);

			if (is_editing)
				RedrawText (true, true);
		}
		#endregion

		#region Activation/Deactivation
		protected override void OnActivated ()
		{
			base.OnActivated ();
			
			// We may need to redraw our text when the color changes
			PintaCore.Palette.PrimaryColorChanged += HandlePintaCorePalettePrimaryColorChanged;
			
			// We always start off not in edit mode
			is_editing = false;
		}

		protected override void OnDeactivated ()
		{
			base.OnDeactivated ();

			// Stop listening for color change events
			PintaCore.Palette.PrimaryColorChanged -= HandlePintaCorePalettePrimaryColorChanged;
			
			StopEditing ();
		}
		#endregion

		#region Mouse Handlers
		protected override void OnMouseDown (DrawingArea canvas, ButtonPressEventArgs args, Cairo.PointD point)
		{
			Point pt = point.ToGdkPoint ();

			// Grab focus so we can get keystrokes
			PintaCore.Chrome.DrawingArea.GrabFocus ();

			// If we're in editing mode, a right click
			// allows you to move the text around
			if (is_editing && (args.Event.Button == 3)) {
				tracking = true;
				startMouseXY = point;
				startClickPoint = clickPoint;

				SetCursor (cursor_hand);
				return;
			} 
			
			// The user clicked the left mouse button			
			if (args.Event.Button == 1) {
				// If we're editing and the user clicked within the text,
				// move the cursor to the click location
				if (is_editing && old_bounds.ContainsCorrect (pt)) {
					Position p = engine.PointToTextPosition (pt);
					engine.SetCursorPosition (p);
					RedrawText (true, true);
					return;
				}

				// We're already editing and the user clicked outside the text,
				// commit the user's work, and start a new edit
				if (is_editing) {
					switch (engine.EditMode) {
						// We were editing, save and stop
						case EditingMode.Editing:
							StopEditing ();
							break;

						// We were editing, but nothing had been
						// keyed. Stop editing.
						case EditingMode.EmptyEdit:
							StopEditing ();
							break;
					}
				}

				// Start editing at the cursor location
				clickPoint = pt;
				StartEditing ();
				engine.Origin = clickPoint;
				RedrawText (true, true);
				PintaCore.Workspace.Invalidate ();
			}
		}

		protected override void OnMouseMove (object o, MotionNotifyEventArgs args, Cairo.PointD point)
		{
			// If we're dragging the text around, do that
			if (tracking) {
				Cairo.PointD delta = new Cairo.PointD (point.X - startMouseXY.X, point.Y - startMouseXY.Y);

				clickPoint = new Point ((int)(startClickPoint.X + delta.X), (int)(startClickPoint.Y + delta.Y));
				engine.Origin = clickPoint;

				RedrawText (true, true);
			}
		}

		protected override void OnMouseUp (Gtk.DrawingArea canvas, Gtk.ButtonReleaseEventArgs args, Cairo.PointD point)
		{
			// If we were dragging the text around, finish that up
			if (tracking) {
				Cairo.PointD delta = new Cairo.PointD (point.X - startMouseXY.X, point.Y - startMouseXY.Y);
				
				clickPoint = new Point ((int)(startClickPoint.X + delta.X), (int)(startClickPoint.Y + delta.Y));
				engine.Origin = clickPoint;

				RedrawText (false, true);
				tracking = false;
				SetCursor (null);
			}
		}
		#endregion

		#region Keyboard Handlers
		protected override void OnKeyDown (DrawingArea canvas, KeyPressEventArgs args)
		{
			Gdk.ModifierType modifier = args.Event.State;

			// If we are dragging the text, we
			// aren't going to handle key presses
			if (tracking)
				return;

			// Ignore anything with Alt pressed
			if ((modifier & Gdk.ModifierType.Mod1Mask) != 0)
				return;

			// Assume that we are going to handle the key
			bool keyHandled = true;

			if (is_editing) {
				switch (args.Event.Key) {
					case Gdk.Key.BackSpace:
						engine.PerformBackspace ();
						break;

					case Gdk.Key.Delete:
						engine.PerformDelete ();
						break;

					case Gdk.Key.KP_Enter:
					case Gdk.Key.Return:
						engine.PerformEnter ();
						break;

					case Gdk.Key.Left:
						engine.PerformLeft ((modifier & Gdk.ModifierType.ControlMask) != 0);
						break;

					case Gdk.Key.Right:
						engine.PerformRight ((modifier & Gdk.ModifierType.ControlMask) != 0);
						break;

					case Gdk.Key.Up:
						engine.PerformUp ();
						break;

					case Gdk.Key.Down:
						engine.PerformDown ();
						break;

					case Gdk.Key.Home:
						engine.PerformHome ((modifier & Gdk.ModifierType.ControlMask) != 0);
						break;

					case Gdk.Key.End:
						engine.PerformEnd ((modifier & Gdk.ModifierType.ControlMask) != 0);
						break;

					case Gdk.Key.Next:
					case Gdk.Key.Prior:
						break;

					case Gdk.Key.Escape:
						StopEditing ();
						break;

					default:
						// Try to handle it as a character
						uint ch = Gdk.Keyval.ToUnicode (args.Event.KeyValue);

						if (ch != 0) {
							engine.InsertCharIntoString (ch);
							RedrawText (true, true);
						} else {
							// We didn't handle the key
							keyHandled = false;
						}

						break;
				}

				// If we processed a key, update the display
				if (keyHandled)
					RedrawText (true, true);

			}

			args.RetVal = keyHandled;
		}
		#endregion

		#region Start/Stop Editing
		private void StartEditing ()
		{
			is_editing = true;
			engine.Clear ();
			PintaCore.Workspace.ActiveDocument.ToolLayer.Hidden = false;
		}

		private void StopEditing ()
		{
			// If we don't have an open document, some of this stuff will crash
			if (!PintaCore.Workspace.HasOpenDocuments)
				return;

			if (!is_editing)
				return;

			Document doc = PintaCore.Workspace.ActiveDocument;

			doc.ToolLayer.Clear ();
			doc.ToolLayer.Hidden = true;

			if (engine.EditMode == EditingMode.Editing) {
				SimpleHistoryItem hist = new SimpleHistoryItem (Icon, Name);
				hist.TakeSnapshotOfLayer (doc.CurrentLayerIndex);

				// Redraw the text without the cursor,
				// and on to the real layer
				RedrawText (false, false);

				doc.History.PushNewItem (hist);
			}

			engine.Clear ();
			doc.Workspace.Invalidate (old_bounds);
			old_bounds = Rectangle.Zero;
			is_editing = false;
		}
		#endregion

		#region Text Drawing Methods
		private void RedrawText (bool showCursor, bool useToolLayer)
		{
			Cairo.ImageSurface surf;
			
			if (!useToolLayer)
				surf = PintaCore.Workspace.ActiveDocument.CurrentLayer.Surface;
			else {
				surf = PintaCore.Workspace.ActiveDocument.ToolLayer.Surface;
				surf.Clear ();
			}
			
			using (var g = new Cairo.Context (surf)) {
				g.Save ();

				g.AppendPath (PintaCore.Workspace.ActiveDocument.SelectionPath);
				g.FillRule = Cairo.FillRule.EvenOdd;
				g.Clip ();

				g.MoveTo (new Cairo.PointD (engine.Origin.X, engine.Origin.Y));
				g.Color = PintaCore.Palette.PrimaryColor;

				// Draw the text
				Pango.CairoHelper.ShowLayout (g, engine.Layout);

				if (showCursor) {
					var loc = engine.GetCursorLocation ();

					g.Antialias = Cairo.Antialias.None;
					g.DrawLine (new Cairo.PointD (loc.X, loc.Y), new Cairo.PointD (loc.X, loc.Y + loc.Height), new Cairo.Color (0, 0, 0, 1), 1);
				}

				g.Restore ();
			}

			Rectangle r = engine.GetLayoutBounds ();
			r.Inflate (10, 10);

			PintaCore.Workspace.Invalidate (old_bounds);
			PintaCore.Workspace.Invalidate (r);

			old_bounds = r;
		}
		#endregion
	}
}
