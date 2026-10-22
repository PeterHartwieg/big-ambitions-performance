using System;
using System.Collections.Generic;
namespace CityPerformance
{
    // One grant covers one audited iterator step, never an entire spawn group.
    public sealed class StepBudget
    {
        public sealed class Ticket
        {
            internal LinkedListNode<Ticket> Node;
            internal int LastFrame = -1;
            public readonly object Owner;
            public readonly double Created;
            public bool Active => Node != null;
            internal Ticket(object owner, double created) { Owner = owner; Created = created; }
        }
        private readonly LinkedList<Ticket> queue = new LinkedList<Ticket>();
        private readonly int capacity, maxSteps;
        private readonly double maxMs;
        private int frame = -1, steps;
        private double elapsed;
        public StepBudget(int capacity, int maxSteps, double maxMs)
        {
            if (capacity < 1 || maxSteps < 1 || maxMs <= 0) throw new ArgumentOutOfRangeException();
            this.capacity = capacity; this.maxSteps = maxSteps; this.maxMs = maxMs;
        }
        public int Count => queue.Count;
        public double OldestAge(double now)
        {
            double oldest = now;
            foreach (var t in queue) oldest = Math.Min(oldest, t.Created);
            return Math.Max(0, now - oldest);
        }
        public Ticket Register(object owner, double now)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            // Replacement invalidates the old ticket, even at capacity.
            var n = queue.First;
            while (n != null) { var next = n.Next; if (ReferenceEquals(n.Value.Owner, owner)) Cancel(n.Value); n = next; }
            if (queue.Count >= capacity) return null;
            var t = new Ticket(owner, now); t.Node = queue.AddLast(t); return t;
        }
        public bool TryBegin(Ticket t, int frameNumber)
        {
            if (frame != frameNumber) { frame = frameNumber; steps = 0; elapsed = 0; }
            if (t == null || !t.Active || queue.First.Value != t || t.LastFrame == frameNumber || steps >= maxSteps || elapsed >= maxMs) return false;
            t.LastFrame = frameNumber; steps++;
            queue.Remove(t.Node); queue.AddLast(t.Node);
            return true;
        }
        // Call in finally, including failed steps. One indivisible step can exceed the time budget.
        public void End(double milliseconds) { if (milliseconds > 0) elapsed += milliseconds; }
        public void Cancel(Ticket t) { if (t == null || !t.Active) return; queue.Remove(t.Node); t.Node = null; }
        public void CancelOwner(object owner)
        {
            var n = queue.First;
            while (n != null) { var next=n.Next; if (ReferenceEquals(n.Value.Owner,owner)) Cancel(n.Value); n=next; }
        }
        public void RemoveInvalid(Func<object,bool> valid)
        {
            var n=queue.First;
            while(n!=null){var next=n.Next;if(!valid(n.Value.Owner))Cancel(n.Value);n=next;}
        }
        public void Clear() { while (queue.First != null) Cancel(queue.First.Value); }
    }
}
