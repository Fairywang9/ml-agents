using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.MLAgents.Actuators
{
    /// <summary>
    /// A class that manages the delegation of events and data structures for IActuators.
    /// </summary>
    internal class ActuatorManager : IList<IActuator>
    {
        IList<IActuator> m_Actuators;

        /// <summary>
        /// Create an ActuatorList with a preset capacity.
        /// </summary>
        /// <param name="capacity">The capacity of the list to create.</param>
        public ActuatorManager(int capacity = 0)
        {
            m_Actuators = new List<IActuator>(capacity);
        }

        /// <summary>
        /// Returns the previously stored actions for the actuators in this list.
        /// </summary>
        public float[] StoredContinuousActions { get; private set; }

        /// <summary>
        /// Returns the previously stored actions for the actuators in this list.
        /// </summary>
        public int[] StoredDiscreteActions { get; private set; }

        /// <summary>
        /// The number of actions calculated by adding <see cref="NumContinuousActions"/> + <see cref="NumDiscreteBranches"/>.
        /// </summary>
        public int TotalNumberOfActions { get; private set; }

        /// <summary>
        /// The sum of all of the discrete branches for all of the <see cref="IActuator"/>s in this manager.
        /// </summary>
        internal int SumOfDiscreteBranchSizes { get; private set; }

        /// <summary>
        /// The number of the discrete branches for all of the <see cref="IActuator"/>s in this manager.
        /// </summary>
        internal int NumDiscreteBranches { get; private set; }

        /// <summary>
        /// The number of continuous actions for all of the <see cref="IActuator"/>s in this manager.
        /// </summary>
        internal int NumContinuousActions { get; private set; }

        BufferedDiscreteActionMask m_DiscreteActionMask;
        bool m_BuffersInitialized;

        public IDiscreteActionMask DiscreteActionMask
        {
            get { return m_DiscreteActionMask; }
        }

        /// <summary>
        /// Allocations the action buffer and ensures that its size matches the number of actions
        /// these collection of IActuators can execute.
        /// </summary>
        internal void EnsureActionBufferSize(IList<IActuator> actuators, int numContinuousActions, int sumOfDiscreteBranches, int numDiscreteBranches)
        {
            Debug.Assert(m_BuffersInitialized == false,
                "Buffers have already been initialized.");
#if DEBUG
            // Make sure the names are actually unique
            // Make sure all Actuators have the same SpaceType
            ValidateActuators();
#endif

            // Sort the Actuators by name to ensure determinism
            SortActuators();
            TotalNumberOfActions = numContinuousActions + numDiscreteBranches;
            StoredContinuousActions = numContinuousActions == 0 ? Array.Empty<float>() : new float[numContinuousActions];
            StoredDiscreteActions = numDiscreteBranches == 0 ? Array.Empty<int>() : new int[numDiscreteBranches];
            m_DiscreteActionMask = new BufferedDiscreteActionMask(actuators, sumOfDiscreteBranches, numDiscreteBranches);
            m_BuffersInitialized = true;
        }

        /// <summary>
        /// Updates the local action buffer with the action buffer passed in.  If the buffer
        /// passed in is null, the local action buffer will be cleared.
        /// </summary>
        /// <param name="continuousActionBuffer">The action buffer which contains all of the
        /// continuous actions for the IActuators in this list.</param>
        /// <param name="discreteActionBuffer">The action buffer which contains all of the
        /// discrete actions for the IActuators in this list.</param>
        public void UpdateActions(float[] continuousActionBuffer, int[] discreteActionBuffer)
        {
            if (!m_BuffersInitialized)
            {
                EnsureActionBufferSize();
            }
            UpdateActionArray(continuousActionBuffer, StoredContinuousActions);
            UpdateActionArray(discreteActionBuffer, StoredDiscreteActions);
        }

        static void UpdateActionArray<T>(T[] sourceActionBuffer, T[] destination)
        {
            if (sourceActionBuffer == null || sourceActionBuffer.Length == 0)
            {
                Array.Clear(destination, 0, destination.Length);
            }
            else
            {
                Debug.Assert(sourceActionBuffer.Length == destination.Length,
                    $"sourceActionBuffer:{sourceActionBuffer.Length} is a different" +
                    $" size than destination: {destination.Length}.");

                Array.Copy(sourceActionBuffer, destination, destination.Length);
            }
        }

        public void WriteActionMask()
        {
            if (!m_BuffersInitialized)
            {
                EnsureActionBufferSize();
            }
            m_DiscreteActionMask.ResetMask();
            var offset = 0;
            for (var i = 0; i < m_Actuators.Count; i++)
            {
                var actuator = m_Actuators[i];
                m_DiscreteActionMask.CurrentBranchOffset = offset;
                actuator.WriteDiscreteActionMask(m_DiscreteActionMask);
                offset += actuator.ActionSpaceDef.NumDiscreteActions;
            }
        }

        /// <summary>
        /// Iterates through all of the IActuators in this list and calls their
        /// <see cref="IActionReceiver.OnActionReceived"/> method on them.
        /// </summary>
        public void ExecuteActions()
        {
            if (!m_BuffersInitialized)
            {
                EnsureActionBufferSize();
            }

            var continuousStart = 0;
            var discreteStart = 0;
            for (var i = 0; i < m_Actuators.Count; i++)
            {
                var actuator = m_Actuators[i];
                var numContinuousActions = actuator.ActionSpaceDef.NumContinuousActions;
                var numDiscreteActions = actuator.ActionSpaceDef.NumDiscreteActions;

                var continuousActions = ActionSegment<float>.Empty;
                if (numContinuousActions > 0)
                {
                    continuousActions = new ActionSegment<float>(StoredContinuousActions,
                        continuousStart,
                        numContinuousActions);
                }

                var discreteActions = ActionSegment<int>.Empty;
                if (numDiscreteActions > 0)
                {
                    discreteActions = new ActionSegment<int>(StoredDiscreteActions,
                        discreteStart,
                        numDiscreteActions);
                }

                actuator.OnActionReceived(new ActionBuffers(continuousActions, discreteActions));
                continuousStart += numContinuousActions;
                discreteStart += numDiscreteActions;
            }
        }

        /// <summary>
        /// Resets the data of the local action buffer to all 0f.
        /// </summary>
        public void ResetData()
        {
            if (m_BuffersInitialized)
            {
                Array.Clear(StoredContinuousActions, 0, StoredContinuousActions.Length);
                Array.Clear(StoredDiscreteActions, 0, StoredDiscreteActions.Length);
                for (var i = 0; i < m_Actuators.Count; i++)
                {
                    m_Actuators[i].ResetData();
                }
            }
        }

        void EnsureActionBufferSize()
        {
            EnsureActionBufferSize(m_Actuators, NumContinuousActions, SumOfDiscreteBranchSizes,
                NumDiscreteBranches);
        }

        /// <summary>
        /// Sorts the <see cref="IActuator"/>s according to their <see cref="IActuator.GetName"/> value.
        /// </summary>
        void SortActuators()
        {
            ((List<IActuator>)m_Actuators).Sort((x,
                y) => x.Name
                .CompareTo(y.Name));
        }

        void ValidateActuators()
        {
            for (var i = 0; i < m_Actuators.Count - 1; i++)
            {
                Debug.Assert(
                    !m_Actuators[i].Name.Equals(m_Actuators[i + 1].Name),
                    "Actuator names must be unique.");
                var first = m_Actuators[i].ActionSpaceDef;
                var second = m_Actuators[i + 1].ActionSpaceDef;
                Debug.Assert(first.NumContinuousActions > 0 == second.NumContinuousActions > 0,
                    "Actuators on the same Agent must have the same action SpaceType.");
            }
        }

        void AddToBufferSizes(IActuator actuatorItem)
        {
            if (actuatorItem == null)
            {
                return;
            }

            NumContinuousActions += actuatorItem.ActionSpaceDef.NumContinuousActions;
            NumDiscreteBranches += actuatorItem.ActionSpaceDef.NumDiscreteActions;
            SumOfDiscreteBranchSizes += actuatorItem.ActionSpaceDef.SumOfDiscreteBranchSizes;
        }

        void SubtractFromBufferSize(IActuator actuatorItem)
        {
            if (actuatorItem == null)
            {
                return;
            }

            NumContinuousActions -= actuatorItem.ActionSpaceDef.NumContinuousActions;
            NumDiscreteBranches -= actuatorItem.ActionSpaceDef.NumDiscreteActions;
            SumOfDiscreteBranchSizes -= actuatorItem.ActionSpaceDef.SumOfDiscreteBranchSizes;
        }

        void ClearBufferSizes()
        {
            NumContinuousActions = NumDiscreteBranches = SumOfDiscreteBranchSizes = 0;
        }

        /*********************************************************************************
         * IList implementation that delegates to m_Actuators List.                      *
         *********************************************************************************/

        /// <summary>
        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
        /// </summary>
        public IEnumerator<IActuator> GetEnumerator()
        {
            return m_Actuators.GetEnumerator();
        }

        /// <summary>
        /// <inheritdoc cref="IList{T}.GetEnumerator"/>
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)m_Actuators).GetEnumerator();
        }

        /// <summary>
        /// <inheritdoc cref="ICollection{T}.Add"/>
        /// </summary>
        /// <param name="item"></param>
        public void Add(IActuator item)
        {
            Debug.Assert(m_BuffersInitialized == false,
                "Cannot add to the ActuatorManager after its buffers have been initialized");
            m_Actuators.Add(item);
            AddToBufferSizes(item);
        }

        /// <summary>
        /// <inheritdoc cref="ICollection{T}.Clear"/>
        /// </summary>
        public void Clear()
        {
            Debug.Assert(m_BuffersInitialized == false,
                "Cannot clear the ActuatorManager after its buffers have been initialized");
            m_Actuators.Clear();
            ClearBufferSizes();
        }

        /// <summary>
        /// <inheritdoc cref="ICollection{T}.Contains"/>
        /// </summary>
        public bool Contains(IActuator item)
        {
            return m_Actuators.Contains(item);
        }

        /// <summary>
        /// <inheritdoc cref="ICollection{T}.CopyTo"/>
        /// </summary>
        public void CopyTo(IActuator[] array, int arrayIndex)
        {
            m_Actuators.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// <inheritdoc cref="ICollection{T}.Remove"/>
        /// </summary>
        public bool Remove(IActuator item)
        {
            Debug.Assert(m_BuffersInitialized == false,
                "Cannot remove from the ActuatorManager after its buffers have been initialized");
            if (m_Actuators.Remove(item))
            {
                SubtractFromBufferSize(item);
                return true;
            }
            return false;
        }

        /// <summary>
        /// <inheritdoc cref="ICollection{T}.Count"/>
        /// </summary>
        public int Count => m_Actuators.Count;

        /// <summary>
        /// <inheritdoc cref="ICollection{T}.IsReadOnly"/>
        /// </summary>
        public bool IsReadOnly => m_Actuators.IsReadOnly;

        /// <summary>
        /// <inheritdoc cref="IList{T}.IndexOf"/>
        /// </summary>
        public int IndexOf(IActuator item)
        {
            return m_Actuators.IndexOf(item);
        }

        /// <summary>
        /// <inheritdoc cref="IList{T}.Insert"/>
        /// </summary>
        public void Insert(int index, IActuator item)
        {
            Debug.Assert(m_BuffersInitialized == false,
                "Cannot insert into the ActuatorManager after its buffers have been initialized");
            m_Actuators.Insert(index, item);
            AddToBufferSizes(item);
        }

        /// <summary>
        /// <inheritdoc cref="IList{T}.RemoveAt"/>
        /// </summary>
        public void RemoveAt(int index)
        {
            Debug.Assert(m_BuffersInitialized == false,
                "Cannot remove from the ActuatorManager after its buffers have been initialized");
            var actuator = m_Actuators[index];
            SubtractFromBufferSize(actuator);
            m_Actuators.RemoveAt(index);
        }

        /// <summary>
        /// <inheritdoc cref="IList{T}.this"/>
        /// </summary>
        public IActuator this[int index]
        {
            get => m_Actuators[index];
            set
            {
                Debug.Assert(m_BuffersInitialized == false,
                    "Cannot modify the ActuatorManager after its buffers have been initialized");
                var old = m_Actuators[index];
                SubtractFromBufferSize(old);
                m_Actuators[index] = value;
                AddToBufferSizes(value);
            }
        }
    }
}
