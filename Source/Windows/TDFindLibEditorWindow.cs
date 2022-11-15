﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using UnityEngine;
using RimWorld;

namespace TD_Find_Lib
{
	public class TDFindLibEditorWindow : Window
	{
		public readonly FindDescriptionDrawer drawer;
		private Action<FindDescription> onCloseIfChanged;

		public TDFindLibEditorWindow(FindDescription desc, Action<FindDescription> onCloseIfChanged = null)
		{
			drawer = new FindDescriptionDrawer(desc) { showName = true };
			onlyOneOfTypeAllowed = false;
			preventCameraMotion = false;
			draggable = true;
			resizeable = true;
			doCloseX = true;
			this.onCloseIfChanged = onCloseIfChanged;
		}

		public override void PostClose()
		{
			if (drawer.findDesc.changed)
				onCloseIfChanged?.Invoke(drawer.findDesc);
		}


		public virtual Vector2 RequestedSize => new Vector2(600, 600);
		public override Vector2 InitialSize
		{
			get
			{
				Vector2 size = RequestedSize;
				if (size.y > (float)(UI.screenHeight - 35))
				{
					size.y = UI.screenHeight - 35;
				}
				if (size.x > (float)UI.screenWidth)
				{
					size.x = UI.screenWidth;
				}
				return size;
			}
		}

		public override void SetInitialSizeAndPosition()
		{
			base.SetInitialSizeAndPosition();
			windowRect.x = 0;
		}


		public override void DoWindowContents(Rect fillRect)
		{
			drawer.DrawFindDescription(fillRect, Find.CurrentMap == null ? null :
				row =>
				{
					FilterStorageUtil.ButtonChooseExportFilter(row, drawer.findDesc, "Save");
					if (row.ButtonIcon(FindTex.List, "List things matching this filter"))
					{
						Find.WindowStack.Add(new TDFindListThingsWindow(drawer.findDesc.CloneForUse(Find.CurrentMap)));
					}
				});
		}
	}
	public class TDFindLibViewerWindow : TDFindLibEditorWindow
	{
		public TDFindLibViewerWindow(FindDescription desc):base(desc)
		{
			drawer.permalocked = true;
		}
	}

	public class FindDescriptionDrawer
	{ 
		public FindDescription findDesc;
		private bool _locked;
		public bool locked
		{
			get => _locked || permalocked;
			set => _locked = value;
		}
		public bool permalocked;
		public bool showName;

		public FindDescriptionDrawer(FindDescription desc)
		{
			findDesc = desc;
		}

		//Draw Filters
		public void DrawFindDescription(Rect rect, Action<WidgetRow> extraIconsDrawer = null)
		{
			Listing_StandardIndent listing = new Listing_StandardIndent()
			{ maxOneColumn = true };

			listing.Begin(rect);


			//Filter Name
			Text.Font = GameFont.Medium;
			Rect nameRect = listing.GetRect(Text.LineHeight);
			if(showName)
				Widgets.Label(nameRect, (permalocked ? "Viewing: " : "Editing: ") + findDesc.name);

			//Buttons
			WidgetRow buttonRow = new WidgetRow(nameRect.xMax - 20, nameRect.yMin, UIDirection.LeftThenDown);

			if (!locked && buttonRow.ButtonIcon(FindTex.Cancel, "ClearAll".Translate()))
				findDesc.Reset();

			if (!permalocked && buttonRow.ButtonIcon(locked ? FindTex.LockOn : FindTex.LockOff, "TD.LockEditing".Translate()))
				locked = !locked;

			if (!locked && showName && buttonRow.ButtonIcon(TexButton.Rename))
				Find.WindowStack.Add(new Dialog_Name(
					findDesc.name, 
					newName => { findDesc.name = newName; findDesc.changed = true; },
					$"Rename {findDesc.name}"));

			if (DebugSettings.godMode)
				buttonRow.Label(findDesc.active ? "ACTIVE!" : "INACTIVE");

			extraIconsDrawer?.Invoke(buttonRow);

			//Listing Type
			Text.Font = GameFont.Small;

			Rect headerRect = listing.GetRect(Text.LineHeight);
			Rect typeRect = headerRect.LeftPart(.6f);
			Rect allMapsRect = headerRect.RightPart(.3f);
			Widgets.DrawHighlightIfMouseover(typeRect);
			Widgets.DrawHighlightIfMouseover(allMapsRect);

			Widgets.Label(typeRect, "TD.Listing".Translate() + findDesc.BaseType.TranslateEnum());
			if (!locked && Widgets.ButtonInvisible(typeRect))
			{
				List<FloatMenuOption> types = new List<FloatMenuOption>();
				foreach (BaseListType type in DebugSettings.godMode ? Enum.GetValues(typeof(BaseListType)) : BaseListNormalTypes.normalTypes)
					types.Add(new FloatMenuOption(type.TranslateEnum(), () => findDesc.BaseType = type));

				Find.WindowStack.Add(new FloatMenu(types));
			}


			bool filterChanged = false;

			//Extra options:
			bool allMaps = findDesc.allMaps;
			Widgets.CheckboxLabeled(allMapsRect,
				"TD.AllMaps".Translate(),
				ref allMaps);
			TooltipHandler.TipRegion(allMapsRect, "TD.CertainFiltersDontWorkForAllMaps-LikeZonesAndAreasThatAreObviouslySpecificToASingleMap".Translate());

			if(!locked && allMaps != findDesc.allMaps)
			{
				findDesc.allMaps = allMaps; //Re-writes map label, remakes list. Hopefully the map is set if allmaps is checked off?
			}

			listing.GapLine();


			//Draw Filters!!!
			Rect listRect = listing.GetRemainingRect();

			//Lock out input to filters.
			if (locked &&
				Event.current.type != EventType.Repaint &&
				Event.current.type != EventType.Layout &&
				Event.current.type != EventType.Ignore &&
				Event.current.type != EventType.Used &&
				Event.current.type != EventType.ScrollWheel &&
				Mouse.IsOver(listRect))
			{
				Event.current.Use();
			}

			//Draw Filters:
			filterChanged |= findDesc.Children.DrawFilters(listRect, locked);

			listing.End();

			//Update if needed
			if (filterChanged)
				findDesc.RemakeList();
		}
	}
}