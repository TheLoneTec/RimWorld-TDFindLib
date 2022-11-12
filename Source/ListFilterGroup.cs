﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace TD_Find_Lib
{
	public class ListFilterGroup : ListFilter, IFilterHolder
	{
		public bool any = true; // or all
		private FilterHolder children;
		public FilterHolder Children => children;

		public ListFilterGroup()
		{
			children = new FilterHolder(this);
		}
		protected override bool FilterApplies(Thing t) =>
			any ? Children.Filters.Any(f => f.Enabled && f.AppliesTo(t)) :
			Children.Filters.All(f => !f.Enabled || f.AppliesTo(t));

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref any, "any", true);

			Children.ExposeData();
		}
		public override ListFilter Clone()
		{
			ListFilterGroup clone = (ListFilterGroup)base.Clone();
			clone.children = children.Clone(clone);
			clone.any = any;

			return clone;
		}

		public override bool DrawMain(Rect rect, bool locked)
		{
			bool changed = false;
			WidgetRow row = new WidgetRow(rect.x, rect.y);
			row.Label("TD.IncludeThingsThatMatch".Translate());
			if (row.ButtonText(any ? "TD.AnyOption".Translate() : "TD.AllOptions".Translate()))
			{
				any = !any;
				changed = true;
			}
			row.Label("TD.OfTheseFilters".Translate());
			return changed;
		}

		protected override bool DrawUnder(Listing_StandardIndent listing, bool locked)
		{
			listing.NestedIndent(Listing_Standard.DefaultIndent);
			listing.Gap();

			//Draw filters
			bool changed = Children.DrawFilters(listing, locked);

			listing.NestedOutdent();
			return changed;
		}
	}

	public class ListFilterInventory : ListFilterGroup
	{
		protected bool holdingThis;//or what I'm holding

		protected override bool FilterApplies(Thing t)
		{
			if (holdingThis)
			{
				IThingHolder parent = t.ParentHolder;
				while (parent.IsValidHolder())
				{
					if (parent is Thing parentThing && base.FilterApplies(parentThing))
						return true;
					parent = parent.ParentHolder;
				}
				return false;
			}
			else
			{
				return ContentsUtility.AllKnownThings(t as IThingHolder).Any(child => base.FilterApplies(child));
			}
		}
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref holdingThis, "holdingThis", true);
		}
		public override ListFilter Clone()
		{
			ListFilterInventory clone = (ListFilterInventory)base.Clone();
			clone.holdingThis = holdingThis;
			return clone;
		}

		public override bool DrawMain(Rect rect, bool locked)
		{
			bool changed = false;
			WidgetRow row = new WidgetRow(rect.x, rect.y);
			if (row.ButtonText(holdingThis ? "TD.TheThingHoldingThis".Translate() : "TD.AnythingThisIsHolding".Translate()))
			{
				changed = true;
				holdingThis = !holdingThis;
			}
			row.Label("TD.Matches".Translate());
			if (row.ButtonText(any ? "TD.AnyOption".Translate() : "TD.AllOptions".Translate()))
			{
				changed = true;
				any = !any;
			}
			row.Label("TD.Of".Translate());
			return changed;
		}
	}

	public class ListFilterNearby: ListFilterGroup
	{
		int range;

		protected override bool FilterApplies(Thing t)
		{
			IntVec3 pos = t.PositionHeld;
			Map map = t.MapHeld;

			CellRect cells = new CellRect(pos.x - range, pos.z - range, range * 2 + 1, range * 2 + 1);
			foreach (IntVec3 p in cells)
				if (map.thingGrid.ThingsAt(p).Any(child => base.FilterApplies(child)))
					return true;
			return false;
		}
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref range, "range");
		}
		public override ListFilter Clone()
		{
			ListFilterNearby clone = (ListFilterNearby)base.Clone();
			clone.range = range;
			return clone;
		}

		public override bool DrawMain(Rect rect, bool locked)
		{
			bool changed = false;
			WidgetRow row = new WidgetRow(rect.x, rect.y);

			row.Label("TD.AnythingXStepsNearbyMatches".Translate());
			if (row.ButtonText(any ? "TD.AnyOption".Translate() : "TD.AllOptions".Translate()))
			{
				any = !any;
				changed = true;
			}

			IntRange slider = new IntRange(0, range);
			rect.xMin = row.FinalX;
			Widgets.IntRange(rect, id, ref slider, max: 10);
			if(range != slider.max)
			{
				range = slider.max;
				changed = true;
			}
			return changed;
		}
	}
}
