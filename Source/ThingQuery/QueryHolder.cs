﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;
using UnityEngine;

namespace TD_Find_Lib
{
	public interface IQueryHolder
	{
		public HeldQueries Children { get; }
		public IQueryHolder Parent { get; }
		public IQueryHolderRoot RootHolder { get; }	//Either return this or a parent
	}
	public interface IQueryHolderDroppable : IQueryHolder
	{
		public bool AcceptsDrops { get; }
	}
	public interface IQueryHolderRoot : IQueryHolderDroppable	//presumably roots will be droppable right?
	{
		public string Name { get; }
		public void NotifyUpdated();
		public void NotifyRefUpdated();
		public bool Active { get; }
		public Map BoundMap { get; }
	}

	// QueryHolder is actually one of the later, untested additions.
	// Everything went through QuerySearch which search on all things in a map
	//  Then I thought "Hey, maybe I should open this up to any list of things"
	// So that's QueryHolder, it simply holds and applies queries to given things.
	public class QueryHolder : IQueryHolderRoot, IExposable
	{
		public string name = "Query Holder";
		public String Name => name;

		// What to search for
		protected HeldQueries children;

		// If you clone a QueryHolder it starts unchanged.
		// Not used directly but good to know if a save is needed.
		public bool changed;
		public virtual void Changed() => changed = true;


		// from IQueryHolder:
		public IQueryHolder Parent => null;
		public virtual IQueryHolderRoot RootHolder => this;
		public Map BoundMap => boundMap;
		public HeldQueries Children => children;
		public virtual void NotifyUpdated() { }
		public virtual void NotifyRefUpdated() => RebindMap();
		public virtual bool Active => false;
		public virtual bool AcceptsDrops => true;

		public QueryHolder()
		{
			children = new(this);
		}

		public virtual void ExposeData()
		{
			children.ExposeData();
		}


		public virtual void Reset()
		{
			changed = true;

			children.Clear();
			UnbindMap();
		}


		// All Or Any Query
		public bool MatchAllQueries
		{
			get => children.matchAllQueries;
			set
			{
				children.matchAllQueries = value;

				//it would be nice if this had an option not to trigger a remake ohwell.
				Changed();
			}
		}


		public QueryHolder CloneAsHolder()
		{
			QueryHolder newHolder = new();

			newHolder.name = name;
			newHolder.children = children.Clone(newHolder);

			return newHolder;
		}

		// Default parameters: not active, all things on current map
		public QuerySearch CloneAsSearch()
		{
			QuerySearch search = new();

			search.name = name;
			search.children = children.Clone(search);

			return search;
		}


		private Map boundMap;
		public void UnbindMap() => boundMap = null;

		public void RebindMap()
		{
			if (boundMap == null) return;

			children.DoResolveRef(boundMap);
		}
		public void BindToMap(Map map)
		{
			if (boundMap == map) return;

			boundMap = map;

			children.DoResolveRef(boundMap);
		}

		// Check if the thing passes the queries.
		// A Map is needed for certain filters like zones and areas.
		public bool AppliesTo(Thing thing, Map map = null)
		{
			if(map != null)
				BindToMap(map);

			return children.AppliesTo(thing);
		}
		public void Filter(ref List<Thing> newListedThings, Map map = null)
		{
			if (map != null)
				BindToMap(map);

			children.Filter(ref newListedThings);
		}



		// Handle esc key before windows do. Also unfocus when leaving the window.
		// This is a roundabout way to hijack the esc-keypress from a window before it closes the window.
		// Any Window displaying this should override OnCancelKeyPressed/Notify_ClickOutsideWindow and call this
		public bool Unfocus()
		{
			return children.Any(f => f.Unfocus());
		}
	}


	public class HeldQueries // : IExposable //Not IExposable because that means ctor QueryHolder() should exist.
	{
		private IQueryHolder parent;
		public List<ThingQuery> queries = new ();
		public bool matchAllQueries = true; // or ANY
		public int anyMin;
		
		public HeldQueries(IQueryHolder p)
		{
			parent = p;
		}

		public void ExposeData()
		{
			Scribe_Collections.Look(ref queries, "queries");
			Scribe_Values.Look(ref matchAllQueries, "matchAllQueries", forceSave: true);  //Force save because the default is different in different contexts
			Scribe_Values.Look(ref anyMin, "anyMin");

			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				if (queries == null)
				{
					queries = new ();
				}
				else
				{
					foreach (var f in queries)
						f.parent = parent;
				}
			}
		}

		public HeldQueries Clone(IQueryHolder newParent)
		{
			HeldQueries clone = new(newParent);

			foreach (var f in queries)
				clone.Add(f.MakeClone(), remake: false);

			clone.matchAllQueries = matchAllQueries;
			clone.anyMin = anyMin;

			return clone;
		}

		public void Import(HeldQueries otherHolder)
		{
			queries = otherHolder.queries;
			matchAllQueries = otherHolder.matchAllQueries;
			anyMin = otherHolder.anyMin;

			foreach (var f in queries)
				f.parent = parent;
		}


		public void DoResolveRef(Map map)
		{
			ForEach(f => f.DoResolveRef(map));
			ForEach(f => f.WarnIfNameSelectionError());
		}


		// Add query and set its parent to this (well, the same parent IQueryHolder of this)
		public void Add(ThingQuery newQuery, int index = -1, bool remake = true, bool focus = false)
		{
			newQuery.parent = parent;
			if(index == -1)
				queries.Add(newQuery);
			else
				queries.Insert(index, newQuery);

			if (focus) newQuery.Focus();
			if (remake) parent.RootHolder?.NotifyUpdated();
		}

		public void Clear()
		{
			queries.Clear();
		}

		public void RemoveAll(HashSet<ThingQuery> removedQueries)
		{
			queries.RemoveAll(f => removedQueries.Contains(f));
		}

		public bool Any(Predicate<ThingQuery> predicate)
		{
			if (parent is ThingQuery f)
				if (predicate(f))
					return true;

			foreach (var query in queries)
			{
				if (query is IQueryHolder childHolder)
				{
					if (childHolder.Children.Any(predicate)) //handles calling on itself
						return true;
				}
				else if (predicate(query))
					return true;
			}

			return false;
		}

		public void DoReorderQuery(int from, int to, bool remake = true)
		{
			var draggedQuery = queries[from];
			if (Event.current.control)
			{
				var newQuery = draggedQuery.MakeClone();
				Add(newQuery, to, remake);
			}
			else
			{
				queries.RemoveAt(from);
				Add(draggedQuery, from < to ? to - 1 : to, remake);
			}
		}

		//Gather method that passes in both QuerySearch and all ThingQuerys to selector
		public IEnumerable<T> Gather<T>(Func<IQueryHolder, T?> selector) where T : struct
		{
			if (selector(parent) is T result)
				yield return result;

			foreach (var query in queries)
				if (query is IQueryHolder childHolder)
					foreach (T r in childHolder.Children.Gather(selector))
						yield return r;
		}
		//sadly 100% copied from above, subtract the "?" oh gee.
		public IEnumerable<T> Gather<T>(Func<IQueryHolder, T> selector) where T : class
		{
			if (selector(parent) is T result)
				yield return result;

			foreach (var query in queries)
				if (query is IQueryHolder childHolder)
					foreach (T r in childHolder.Children.Gather(selector))
						yield return r;
		}

		// Do action on all IQueryHolder
		public void ForEach(Action<IQueryHolder> action)
		{
			action(parent);

			foreach (var query in queries)
				if (query is IQueryHolder childHolder)
					childHolder.Children.ForEach(action); //handles calling on itself
		}

		// Do action on all ThingQuery
		public void ForEach(Action<ThingQuery> action)
		{
			if(parent is ThingQuery f)
				action(f);
			foreach (var query in queries)
			{
				if (query is IQueryHolder childHolder)
					childHolder.Children.ForEach(action); //handles calling on itself
				else //just a query then
					action(query);
			}
		}

		public void MasterReorder(int from, int fromGroup, int to, int toGroup)
		{
			Log.Message($"QueryHolder.MasterReorder(int from={from}, int fromGroup={fromGroup}, int to={to}, int toGroup={toGroup})");

			ThingQuery draggedQuery = Gather(delegate (IQueryHolder holder)
			{
				if (holder.Children.reorderID == fromGroup && holder is IQueryHolderDroppable dropper && dropper.AcceptsDrops)
					return holder.Children.queries.ElementAt(from);

				return null;
			}).First();

			IQueryHolder newHolder = null;
			ForEach(delegate (IQueryHolder holder)
			{
				if (holder.Children.reorderID == toGroup && holder is IQueryHolderDroppable dropper && dropper.AcceptsDrops)
				{
					// Hold up, don't drop inside yourself or any of your sacred lineage
					for(IQueryHolder ancestor = holder; ancestor != null; ancestor = ancestor.Parent)
						if (draggedQuery == ancestor)
							return;

					newHolder = holder; //todo: abort early?
				}
			});

			if (newHolder != null)
			{
				if (Event.current.control)
				{
					newHolder.Children.Add(draggedQuery.MakeClone(), to);
				}
				else
				{
					draggedQuery.parent.Children.queries.Remove(draggedQuery);
					newHolder.Children.Add(draggedQuery, to);
				}
			}
		}

		//Draw queries completely, in a rect
		public bool DrawQueriesInRect(Rect listRect, bool locked, ref Vector2 scrollPositionFilt, ref float scrollHeight)
		{
			Listing_StandardIndent listing = new()
				{ maxOneColumn = true };

			float viewWidth = listRect.width;
			if (scrollHeight > listRect.height)
				viewWidth -= 16f;
			Rect viewRect = new(0f, 0f, viewWidth, scrollHeight);

			listing.BeginScrollView(listRect, ref scrollPositionFilt, viewRect);

			bool changed = DrawQueriesListing(listing, locked, extendableHeight: listRect.height);

			List<int> reorderIDs = new(Gather<int>(f => f.Children.reorderID));

			ReorderableWidget.NewMultiGroup(reorderIDs, parent.RootHolder.Children.MasterReorder);

			listing.EndScrollView(ref scrollHeight);

			return changed;
		}


		// draw queries continuing a Listing_StandardIndent
		public int reorderID = -1;
		private float reorderRectHeight;

		public bool DrawQueriesListing(Listing_StandardIndent listing, bool locked, string indentAfterFirst = null, float extendableHeight = 0)
		{
			float startHeight = listing.CurHeight;

			if (Event.current.type == EventType.Repaint)
			{
				Rect reorderRect = new(0f, startHeight, listing.ColumnWidth, reorderRectHeight);
				reorderID = ReorderableWidget.NewGroup(
					(int from, int to) => DoReorderQuery(from, to, true),
					ReorderableDirection.Vertical,
					reorderRect, 1f,
					extraDraggedItemOnGUI: (int index, Vector2 dragStartPos) =>
						DrawMouseAttachedQuery(queries[index], reorderRect.width - 100));
			}

			bool changed = false;
			HashSet<ThingQuery> removedQueries = new();
			bool first = true;
			foreach (ThingQuery query in queries)
			{
				(bool ch, bool d) = query.Listing(listing, locked);
				changed |= ch;
				if (d)
					removedQueries.Add(query);

				// Layout event in query.Listing will set query.usedRect ; Repaint in Reorderable will use it.
				// We'd like the reorderable of the parent to come before children
				// so children will be last in the list, and will override the selected clicked rect
				// But events in the children should be caught and used before dragging is allowed, so it has to go after.
				ReorderableWidget.Reorderable(reorderID, query.queryRect);

				if(first)
				{
					first = false;
					if (indentAfterFirst != null)
						listing.NestedIndent(indentAfterFirst);
				}
			}
			// do the indent with no objects for the "Add new"
			if (first && indentAfterFirst != null)
					listing.NestedIndent(indentAfterFirst);

			RemoveAll(removedQueries);

			if (!locked)
				DrawAddRow(listing);

			reorderRectHeight = listing.CurHeight - startHeight;
			if (extendableHeight > reorderRectHeight)
				reorderRectHeight = extendableHeight;

			if (indentAfterFirst != null)
				listing.NestedOutdent();

			return changed;
		}

		public static void DrawMouseAttachedQuery(ThingQuery dragQuery, float width)
		{
			Vector2 mousePositionOffset = Event.current.mousePosition + Vector2.one * 12;
			Rect dragRect = new(mousePositionOffset, new(width, Text.LineHeight + 2));//not sure where the constant for listing.verticalSpacing is

			//Same id 34003428 as GenUI.DrawMouseAttachment
			Find.WindowStack.ImmediateWindow(34003428, dragRect, WindowLayer.Super,
				() => dragQuery.DrawRow(dragRect.AtZero()),
				doBackground: false, absorbInputAroundWindow: false, 0f);
		}

		public void DrawAddRow(Listing_StandardIndent listing)
		{
			Rect addRow = listing.GetRect(Text.LineHeight);
			listing.Gap(listing.verticalSpacing);

			if (ReorderableWidget.Dragging)
				return;

			Rect butRect = addRow; butRect.width = Text.LineHeight;
			Widgets.DrawTextureFitted(butRect, TexButton.Plus, 1.0f);

			Rect textRect = addRow; textRect.xMin += Text.LineHeight + WidgetRow.DefaultGap;
			Widgets.Label(textRect, "TD.AddNewQuery...".Translate());

			Widgets.DrawHighlightIfMouseover(addRow);

			if (Widgets.ButtonInvisible(addRow))
			{
				DoFloatAllQueries();
			}
		}

		public void DoFloatAllQueries()
		{
			DoFloatAllQueries(ThingQueryMaker.RootQueries);
		}

		public void DoFloatAllQueries(IEnumerable<ThingQuerySelectableDef> defs)
		{
			List<FloatMenuOption> options = new();

			foreach (ThingQuerySelectableDef def in defs.Where(d => d.Visible()))
				if(FloatFor(def) is FloatMenuOption opt)
					options.Add(opt);

			Find.WindowStack.Add(new FloatMenu(options));
		}

		public FloatMenuOption FloatFor(ThingQuerySelectableDef def)
		{
			if (def is ThingQueryDef fDef)
				return new FloatMenuOption(
					fDef.GetLabel(),
					() => Add(ThingQueryMaker.MakeQuery(fDef), focus: true)
				);
			else if (def is ThingQueryPreselectDef pDef)
				return new FloatMenuOption(
					pDef.GetLabel(),
					() => Add(ThingQueryMaker.MakeQuery(pDef), focus: true)
				);
			else if (def is ThingQueryCategoryDef cDef)
			{
				int count = cDef.subQueries.Count(d => d.Visible());
				if (count == 0)
				{
					return null; // whoops my mistake I'll let myself out (this gonna be mod category with no mods)
				}
				if (count == 1)
				{
					return FloatFor(cDef.subQueries.First());
				}
				else
				{
					return new FloatMenuOption(
						"+ " + cDef.GetLabel(),
						() => DoFloatAllQueries(cDef.subQueries)
					);
				}
			}

			return null; //don't do this though
		}


		// APPLY THE QUERIES!
		public bool AppliesTo(Thing t) =>
			matchAllQueries ? queries.All(f => !f.Enabled || f.AppliesTo(t)) :
				queries.AnyX(f => f.Enabled && f.AppliesTo(t), anyMin);


		// Apply to a list of things
		private static List<Thing> _newFilteredThings = new();
		private static List<ThingQuery> _enabledQueries = new();
		public void Filter(ref List<Thing> newListedThings)
		{
			_enabledQueries.AddRange(queries.Where(q => q.Enabled));

			if (matchAllQueries)
			{
				// ALL
				foreach (ThingQuery query in _enabledQueries)
				{
					// Clears newQueriedThings, fills with newListedThings which pass the query.
					query.Apply(newListedThings, _newFilteredThings);

					// newQueriedThings is now the list of things ; swap them
					(newListedThings, _newFilteredThings) = (_newFilteredThings, newListedThings);
				}
			}
			else
			{
				// ANY
				_newFilteredThings.Clear();
				foreach (Thing thing in newListedThings)
					if (_enabledQueries.AnyX(f => f.AppliesTo(thing), anyMin))
						_newFilteredThings.Add(thing);

				(newListedThings, _newFilteredThings) = (_newFilteredThings, newListedThings);
			}

			_enabledQueries.Clear();
			_newFilteredThings.Clear();
		}
	}
}
