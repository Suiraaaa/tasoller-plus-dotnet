using System;
using System.Collections.Generic;

namespace TasollerPlus.Connections
{
    internal sealed class TasollerPlusInputBuffer
    {
        private readonly int _capacity;
        private readonly Queue<TasollerPlusInputFrame> _pendingInputs;
        private TasollerPlusInputFrame? _latestInput;
        private long _droppedInputCount;

        public TasollerPlusInputBuffer(int capacity)
        {
            _capacity = capacity;

            _pendingInputs = new Queue<TasollerPlusInputFrame>(_capacity);
        }

        public void Add(TasollerPlusInputFrame frame)
        {
            if (_pendingInputs.Count == _capacity)
            {
                _pendingInputs.Dequeue();
                _droppedInputCount++;
            }

            _pendingInputs.Enqueue(frame);
            _latestInput = frame;
        }

        public bool TryGetLatest(out TasollerPlusInputFrame frame)
        {
            frame = _latestInput.GetValueOrDefault();
            return _latestInput.HasValue;
        }

        public TasollerPlusInputReadResult ReadPending(TasollerPlusInputFrame[] destination)
        {
            var count = Math.Min(destination.Length, _pendingInputs.Count);
            for (var index = 0; index < count; index++)
            {
                destination[index] = _pendingInputs.Dequeue();
            }

            var result = new TasollerPlusInputReadResult(count, _droppedInputCount);
            _droppedInputCount = default;
            return result;
        }

        public void Clear()
        {
            _latestInput = null;
            _pendingInputs.Clear();
            _droppedInputCount = default;
        }
    }
}
