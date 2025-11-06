// API URL - always use localhost:5010 since the browser makes the API calls
// The orchestrator is exposed on localhost:5010 from Docker
const API_BASE_URL = 'http://localhost:5010';
const POLL_INTERVAL = 2000; // 2 seconds

let activeSagas = new Map();
let pollInterval = null;

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    // Set default time slot to 7 days from now
    const futureDate = new Date();
    futureDate.setDate(futureDate.getDate() + 7);
    document.getElementById('timeSlot').value = futureDate.toISOString().slice(0, 16);
    
    // Load all sagas from the API
    loadAllSagas();
    
    startPolling();
    
    // Render architecture diagram
    renderArchitectureDiagram();
});

function loadAllSagas() {
    fetch(`${API_BASE_URL}/api/saga`, {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json'
        },
        mode: 'cors'
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            return response.json();
        })
        .then(sagas => {
            if (Array.isArray(sagas)) {
                sagas.forEach(saga => {
                    if (saga.sagaId) {
                        // Ensure statusCode is a number
                        const statusCode = typeof saga.status === 'number' ? saga.status : parseInt(saga.status, 10);
                        activeSagas.set(saga.sagaId, {
                            sagaId: saga.sagaId,
                            status: getStatusName(statusCode),
                            statusCode: statusCode,
                            bookingId: saga.bookingId,
                            paymentId: saga.paymentId,
                            rentalId: saga.rentalId,
                            failureReason: saga.failureReason,
                            customerId: saga.customerId,
                            timeSlot: saga.timeSlot,
                            serviceType: saga.serviceType,
                            price: saga.price,
                            updatedAt: saga.updatedAt,
                            scenario: 'Database' // Mark as loaded from database
                        });
                    }
                });
                updateSagasList();
            }
        })
        .catch(error => {
            console.error('Error loading sagas:', error);
            console.error('Error details:', {
                message: error.message,
                stack: error.stack,
                name: error.name
            });
            // Show user-friendly error
            const sagasList = document.getElementById('sagasList');
            if (sagasList) {
                sagasList.innerHTML = `<p class="empty-state error">Failed to load sagas: ${error.message}. Check console for details.</p>`;
            }
        });
}

function startSaga(scenario) {
    let customerId = document.getElementById('customerId').value;
    if (!customerId) {
        customerId = generateGuid();
        document.getElementById('customerId').value = customerId; // Update the input field
    }
    
    // Validate GUID format
    const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!guidRegex.test(customerId)) {
        alert('Customer ID must be a valid GUID format (e.g., 123e4567-e89b-12d3-a456-426614174000)');
        return;
    }
    
    const timeSlot = document.getElementById('timeSlot').value;
    const serviceType = document.getElementById('serviceType').value || 'Oil Change';
    const price = parseFloat(document.getElementById('price').value) || 50.00;
    
    if (!timeSlot) {
        alert('Please select a time slot');
        return;
    }
    
    const endpoint = scenario === 'success' 
        ? '/api/saga/demo/success'
        : `/api/saga/demo/${scenario}`;
    
    const requestBody = {
        customerId: customerId,
        timeSlot: new Date(timeSlot).toISOString(),
        serviceType: serviceType,
        price: price
    };
    
    fetch(`${API_BASE_URL}${endpoint}`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(requestBody)
    })
    .then(response => response.json())
    .then(data => {
        if (data.sagaId) {
            activeSagas.set(data.sagaId, {
                sagaId: data.sagaId,
                status: 'Started',
                statusCode: 0, // Started status
                scenario: scenario,
                createdAt: new Date()
            });
            updateSagasList();
            updateFlowDiagram(data.sagaId);
        }
    })
    .catch(error => {
        console.error('Error starting saga:', error);
        alert('Failed to start saga. Make sure the orchestrator is running.');
    });
}

function startPolling() {
    if (pollInterval) {
        clearInterval(pollInterval);
    }
    
    pollInterval = setInterval(() => {
        activeSagas.forEach((saga, sagaId) => {
            // Only poll sagas that are not in a final state (Completed or Failed)
            // Status 8 = Completed, Status 10 = Failed
            // Ensure statusCode is a number for comparison
            const statusCode = typeof saga.statusCode === 'number' ? saga.statusCode : parseInt(saga.statusCode, 10);
            if (statusCode !== 8 && statusCode !== 10 && !isNaN(statusCode)) {
                fetchSagaStatus(sagaId);
            } else if (statusCode === 8 || statusCode === 10) {
                // Saga is in final state, skip polling
                console.log(`Skipping poll for saga ${sagaId} - status: ${getStatusName(statusCode)} (${statusCode})`);
            }
        });
    }, POLL_INTERVAL);
}

function fetchSagaStatus(sagaId) {
    fetch(`${API_BASE_URL}/api/saga/${sagaId}`)
        .then(response => {
            if (!response.ok) {
                if (response.status === 404) {
                    activeSagas.delete(sagaId);
                    updateSagasList();
                }
                return null;
            }
            return response.json();
        })
        .then(data => {
            if (data) {
                const saga = activeSagas.get(sagaId);
                if (saga) {
                    // Ensure statusCode is a number
                    const statusCode = typeof data.status === 'number' ? data.status : parseInt(data.status, 10);
                    saga.status = getStatusName(statusCode);
                    saga.statusCode = statusCode;
                    saga.bookingId = data.bookingId;
                    saga.paymentId = data.paymentId;
                    saga.rentalId = data.rentalId;
                    saga.failureReason = data.failureReason;
                    saga.customerId = data.customerId;
                    saga.timeSlot = data.timeSlot;
                    saga.serviceType = data.serviceType;
                    saga.price = data.price;
                    saga.updatedAt = data.updatedAt;
                    
                    activeSagas.set(sagaId, saga);
                    updateSagasList();
                    updateFlowDiagram(sagaId);
                    
                    // If saga is in a final state (Completed or Failed), stop polling it
                    // Status 8 = Completed, Status 10 = Failed
                    if (statusCode === 8 || statusCode === 10) {
                        console.log(`Saga ${sagaId} reached final state (${getStatusName(statusCode)}), stopping polling`);
                    }
                }
            }
        })
        .catch(error => {
            console.error(`Error fetching saga ${sagaId}:`, error);
        });
}

function getStatusName(statusCode) {
    const statusMap = {
        0: 'Started',
        1: 'BookingInProgress',
        2: 'BookingCompleted',
        3: 'PaymentInProgress',
        4: 'PaymentCompleted',
        5: 'RentalInProgress',
        6: 'RentalCompleted',
        7: 'NotificationsInProgress',
        8: 'Completed',
        9: 'Compensating',
        10: 'Failed'
    };
    return statusMap[statusCode] || 'Unknown';
}

function updateSagasList() {
    const sagasList = document.getElementById('sagasList');
    
    if (activeSagas.size === 0) {
        sagasList.innerHTML = '<p class="empty-state">No active sagas. Start a saga to see it here.</p>';
        return;
    }
    
    sagasList.innerHTML = '';
    
    // Convert Map to array and sort by creation/update time (newest first)
    const sagasArray = Array.from(activeSagas.entries()).map(([sagaId, saga]) => ({
        sagaId,
        saga,
        sortTime: saga.updatedAt ? new Date(saga.updatedAt) : (saga.createdAt ? new Date(saga.createdAt) : new Date(0))
    }));
    
    // Sort by time descending (newest first)
    sagasArray.sort((a, b) => b.sortTime - a.sortTime);
    
    // Render sagas in reverse order (newest first)
    sagasArray.forEach(({ sagaId, saga }) => {
        const sagaCard = createSagaCard(saga, sagaId);
        sagasList.appendChild(sagaCard);
    });
}

function createSagaCard(saga, sagaId) {
    const card = document.createElement('div');
    card.className = `saga-card ${getCardClass(saga.statusCode)}`;
    card.style.cursor = 'pointer';
    card.onclick = () => showSagaDetails(sagaId);
    
    const statusBadge = document.createElement('span');
    statusBadge.className = `status-badge ${saga.status.toLowerCase().replace(/\s+/g, '-')}`;
    statusBadge.textContent = saga.status;
    
    const header = document.createElement('div');
    header.className = 'saga-header';
    header.innerHTML = `
        <div>
            <strong>Saga ID:</strong>
            <span class="saga-id">${sagaId}</span>
        </div>
        ${statusBadge.outerHTML}
    `;
    
    const details = document.createElement('div');
    details.className = 'saga-details';
    details.innerHTML = `
        <div class="saga-detail">
            <label>Scenario</label>
            <value>${saga.scenario || 'Standard'}</value>
        </div>
        <div class="saga-detail">
            <label>Customer ID</label>
            <value>${saga.customerId ? saga.customerId.substring(0, 8) + '...' : 'N/A'}</value>
        </div>
        <div class="saga-detail">
            <label>Service Type</label>
            <value>${saga.serviceType || 'N/A'}</value>
        </div>
        <div class="saga-detail">
            <label>Price</label>
            <value>$${saga.price ? saga.price.toFixed(2) : '0.00'}</value>
        </div>
        ${saga.bookingId && saga.bookingId !== '00000000-0000-0000-0000-000000000000' ? `
        <div class="saga-detail">
            <label>Booking ID</label>
            <value>${saga.bookingId.substring(0, 8)}...</value>
        </div>
        ` : ''}
        ${saga.paymentId && saga.paymentId !== '00000000-0000-0000-0000-000000000000' ? `
        <div class="saga-detail">
            <label>Payment ID</label>
            <value>${saga.paymentId.substring(0, 8)}...</value>
        </div>
        ` : ''}
        ${saga.rentalId && saga.rentalId !== '00000000-0000-0000-0000-000000000000' ? `
        <div class="saga-detail">
            <label>Rental ID</label>
            <value>${saga.rentalId.substring(0, 8)}...</value>
        </div>
        ` : ''}
    `;
    
    card.appendChild(header);
    card.appendChild(details);
    
    if (saga.failureReason) {
        const failureDiv = document.createElement('div');
        failureDiv.className = 'failure-reason';
        failureDiv.innerHTML = `<strong>Failure Reason:</strong> ${saga.failureReason}`;
        card.appendChild(failureDiv);
    }
    
    return card;
}

function getCardClass(statusCode) {
    if (statusCode === 10) return 'failed';
    if (statusCode === 9) return 'compensating';
    if (statusCode === 8) return 'completed';
    if (statusCode >= 1 && statusCode < 8) return 'active';
    return '';
}

async function updateFlowDiagram(sagaId) {
    const saga = activeSagas.get(sagaId);
    if (!saga) return;
    
    const forwardSteps = document.querySelectorAll('.forward-flow .flow-step');
    const forwardArrows = document.querySelectorAll('.forward-flow .flow-arrow');
    const compensationSteps = document.querySelectorAll('.compensation-flow .flow-step');
    const compensationArrows = document.querySelectorAll('.compensation-flow .flow-arrow');
    const compensationFlow = document.querySelector('.compensation-flow');
    
    // Reset all steps
    forwardSteps.forEach(step => {
        step.classList.remove('active', 'completed', 'failed');
    });
    forwardArrows.forEach(arrow => {
        arrow.classList.remove('active');
    });
    compensationSteps.forEach(step => {
        step.classList.remove('active', 'completed', 'disabled');
    });
    compensationArrows.forEach(arrow => {
        arrow.classList.remove('active');
    });
    
    // Fetch events to check for compensation completion
    let compensationEvents = {};
    try {
        const eventsResponse = await fetch(`${API_BASE_URL}/api/saga/${sagaId}/events`);
        if (eventsResponse.ok) {
            const events = await eventsResponse.json();
            events.forEach(event => {
                if (event.eventType === 'RentalCompensated') {
                    compensationEvents.rental = true;
                } else if (event.eventType === 'PaymentCompensated') {
                    compensationEvents.payment = true;
                } else if (event.eventType === 'BookingCompensated') {
                    compensationEvents.booking = true;
                }
            });
        }
    } catch (error) {
        console.error(`Error fetching events for saga ${sagaId}:`, error);
    }
    
    // Update based on status
    const statusCode = saga.statusCode;
    
    if (statusCode === 10) {
        // Failed - show compensation flow
        if (compensationFlow) {
            compensationFlow.style.display = 'flex';
        }
        
        // Mark forward flow steps
        // Determine which step failed by checking which IDs exist
        // If rentalId exists, rental succeeded (shouldn't happen in failed state)
        // If paymentId exists but rentalId doesn't, rental failed (payment and booking succeeded)
        // If bookingId exists but paymentId doesn't, payment failed (booking succeeded)
        // If bookingId doesn't exist, booking failed
        
        const hasRentalId = saga.rentalId && saga.rentalId !== '00000000-0000-0000-0000-000000000000';
        const hasPaymentId = saga.paymentId && saga.paymentId !== '00000000-0000-0000-0000-000000000000';
        const hasBookingId = saga.bookingId && saga.bookingId !== '00000000-0000-0000-0000-000000000000';
        
        if (hasPaymentId && !hasRentalId) {
            // Rental failed - payment and booking succeeded
            // Note: No rental was booked, so there's nothing to compensate for rental
            forwardSteps[3].classList.add('failed'); // Rental failed
            forwardSteps[2].classList.add('completed'); // Payment completed
            forwardSteps[1].classList.add('completed'); // Booking completed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow - only compensate steps that actually succeeded
            // Skip rental compensation (rental never succeeded, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            if (compensationEvents.payment) {
                compensationSteps[1].classList.add('completed'); // Payment compensated
            } else {
                compensationSteps[1].classList.add('active'); // Compensate payment
            }
            if (compensationEvents.booking) {
                compensationSteps[2].classList.add('completed'); // Booking compensated
            } else {
                compensationSteps[2].classList.add('active'); // Compensate booking
            }
            compensationSteps[3].classList.add('completed'); // Failed
            if (!compensationEvents.payment) {
                compensationArrows[1].classList.add('active');
            }
            if (!compensationEvents.booking) {
                compensationArrows[2].classList.add('active');
            }
        } else if (hasRentalId) {
            // Rental succeeded but something failed after (e.g., notifications failed)
            // This shouldn't happen in failed state, but handle it
            forwardSteps[3].classList.add('completed'); // Rental completed
            forwardSteps[2].classList.add('completed'); // Payment completed
            forwardSteps[1].classList.add('completed'); // Booking completed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow - compensate all steps that succeeded
            if (compensationEvents.rental) {
                compensationSteps[0].classList.add('completed'); // Rental compensated
            } else {
                compensationSteps[0].classList.add('active'); // Compensate rental
            }
            if (compensationEvents.payment) {
                compensationSteps[1].classList.add('completed'); // Payment compensated
            } else {
                compensationSteps[1].classList.add('active'); // Compensate payment
            }
            if (compensationEvents.booking) {
                compensationSteps[2].classList.add('completed'); // Booking compensated
            } else {
                compensationSteps[2].classList.add('active'); // Compensate booking
            }
            compensationSteps[3].classList.add('completed'); // Failed
            if (!compensationEvents.rental) {
                compensationArrows[0].classList.add('active');
            }
            if (!compensationEvents.payment) {
                compensationArrows[1].classList.add('active');
            }
            if (!compensationEvents.booking) {
                compensationArrows[2].classList.add('active');
            }
        } else if (hasPaymentId) {
            // Payment completed but something failed after (shouldn't happen in failed state, but handle it)
            forwardSteps[2].classList.add('completed'); // Payment completed
            forwardSteps[1].classList.add('completed'); // Booking completed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow
            // Skip rental compensation (rental never succeeded, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            if (compensationEvents.payment) {
                compensationSteps[1].classList.add('completed'); // Payment compensated
            } else {
                compensationSteps[1].classList.add('active'); // Compensate payment
            }
            if (compensationEvents.booking) {
                compensationSteps[2].classList.add('completed'); // Booking compensated
            } else {
                compensationSteps[2].classList.add('active'); // Compensate booking
            }
            compensationSteps[3].classList.add('completed'); // Failed
            if (!compensationEvents.payment) {
                compensationArrows[1].classList.add('active');
            }
            if (!compensationEvents.booking) {
                compensationArrows[2].classList.add('active');
            }
        } else if (hasBookingId) {
            // Payment failed - booking succeeded
            forwardSteps[2].classList.add('failed'); // Payment failed
            forwardSteps[1].classList.add('completed'); // Booking completed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow
            // Skip rental and payment compensation (they never succeeded, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            compensationSteps[1].classList.add('disabled'); // Payment compensation not needed (payment failed)
            if (compensationEvents.booking) {
                compensationSteps[2].classList.add('completed'); // Booking compensated
            } else {
                compensationSteps[2].classList.add('active'); // Compensate booking
            }
            compensationSteps[3].classList.add('completed'); // Failed
            if (!compensationEvents.booking) {
                compensationArrows[2].classList.add('active');
            }
        } else {
            // Booking failed
            forwardSteps[1].classList.add('failed'); // Booking failed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow
            // Skip all compensation (booking failed, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            compensationSteps[1].classList.add('disabled'); // Payment compensation not needed
            compensationSteps[2].classList.add('disabled'); // Booking compensation not needed (booking failed)
            compensationSteps[3].classList.add('completed'); // Failed
        }
    } else if (statusCode === 8) {
        // Completed - hide compensation flow, mark all forward steps as completed
        if (compensationFlow) {
            compensationFlow.style.display = 'none';
        }
        forwardSteps.forEach(step => step.classList.add('completed'));
        forwardArrows.forEach(arrow => arrow.classList.add('active'));
    } else if (statusCode === 9) {
        // Compensating - show compensation flow
        if (compensationFlow) {
            compensationFlow.style.display = 'flex';
        }
        
        // Mark forward flow steps
        // Determine which step failed by checking which IDs exist
        const hasRentalId = saga.rentalId && saga.rentalId !== '00000000-0000-0000-0000-000000000000';
        const hasPaymentId = saga.paymentId && saga.paymentId !== '00000000-0000-0000-0000-000000000000';
        const hasBookingId = saga.bookingId && saga.bookingId !== '00000000-0000-0000-0000-000000000000';
        
        if (hasPaymentId && !hasRentalId) {
            // Rental failed - payment and booking succeeded
            // Note: No rental was booked, so there's nothing to compensate for rental
            forwardSteps[3].classList.add('failed'); // Rental failed
            forwardSteps[2].classList.add('completed'); // Payment completed
            forwardSteps[1].classList.add('completed'); // Booking completed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow - only compensate steps that actually succeeded
            // Skip rental compensation (rental never succeeded, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            if (compensationEvents.payment) {
                compensationSteps[1].classList.add('completed'); // Payment compensated
            } else {
                compensationSteps[1].classList.add('active'); // Compensate payment
            }
            if (!compensationEvents.payment) {
                compensationArrows[1].classList.add('active');
            }
        } else if (hasPaymentId) {
            // Payment completed but something failed after (shouldn't happen in compensating state, but handle it)
            forwardSteps[2].classList.add('completed'); // Payment completed
            forwardSteps[1].classList.add('completed'); // Booking completed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow
            // Skip rental compensation (rental never succeeded, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            if (compensationEvents.payment) {
                compensationSteps[1].classList.add('completed'); // Payment compensated
            } else {
                compensationSteps[1].classList.add('active'); // Compensate payment
            }
            if (!compensationEvents.payment) {
                compensationArrows[1].classList.add('active');
            }
        } else if (hasBookingId) {
            // Payment failed - booking succeeded
            forwardSteps[2].classList.add('failed'); // Payment failed
            forwardSteps[1].classList.add('completed'); // Booking completed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow
            // Skip rental and payment compensation (they never succeeded, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            compensationSteps[1].classList.add('disabled'); // Payment compensation not needed
            if (compensationEvents.booking) {
                compensationSteps[2].classList.add('completed'); // Booking compensated
            } else {
                compensationSteps[2].classList.add('active'); // Compensate booking
            }
            if (!compensationEvents.booking) {
                compensationArrows[2].classList.add('active');
            }
        } else {
            // Booking failed
            forwardSteps[1].classList.add('failed'); // Booking failed
            forwardSteps[0].classList.add('completed'); // Start completed
            
            // Show compensation flow
            // Skip all compensation (booking failed, so nothing to compensate)
            compensationSteps[0].classList.add('disabled'); // Rental compensation not needed
            compensationSteps[1].classList.add('disabled'); // Payment compensation not needed
            compensationSteps[2].classList.add('disabled'); // Booking compensation not needed (booking failed)
            compensationSteps[3].classList.add('completed'); // Failed
        }
    } else {
        // Active - hide compensation flow, show forward flow
        if (compensationFlow) {
            compensationFlow.style.display = 'none';
        }
        
        // Active - mark current step
        if (statusCode >= 7) {
            forwardSteps[4].classList.add('active'); // Notifications
            forwardSteps[3].classList.add('completed'); // Rental
            forwardSteps[2].classList.add('completed'); // Payment
            forwardSteps[1].classList.add('completed'); // Booking
            forwardSteps[0].classList.add('completed'); // Start
            forwardArrows[4].classList.add('active');
        } else if (statusCode >= 6) {
            forwardSteps[3].classList.add('completed'); // Rental
            forwardSteps[2].classList.add('completed'); // Payment
            forwardSteps[1].classList.add('completed'); // Booking
            forwardSteps[0].classList.add('completed'); // Start
            forwardArrows[3].classList.add('active');
        } else if (statusCode >= 5) {
            forwardSteps[3].classList.add('active'); // Rental
            forwardSteps[2].classList.add('completed'); // Payment
            forwardSteps[1].classList.add('completed'); // Booking
            forwardSteps[0].classList.add('completed'); // Start
            forwardArrows[2].classList.add('active');
        } else if (statusCode >= 4) {
            forwardSteps[2].classList.add('completed'); // Payment
            forwardSteps[1].classList.add('completed'); // Booking
            forwardSteps[0].classList.add('completed'); // Start
            forwardArrows[2].classList.add('active');
        } else if (statusCode >= 3) {
            forwardSteps[2].classList.add('active'); // Payment
            forwardSteps[1].classList.add('completed'); // Booking
            forwardSteps[0].classList.add('completed'); // Start
            forwardArrows[1].classList.add('active');
        } else if (statusCode >= 2) {
            forwardSteps[1].classList.add('completed'); // Booking
            forwardSteps[0].classList.add('completed'); // Start
            forwardArrows[1].classList.add('active');
        } else if (statusCode >= 1) {
            forwardSteps[1].classList.add('active'); // Booking
            forwardSteps[0].classList.add('completed'); // Start
            forwardArrows[0].classList.add('active');
        } else {
            forwardSteps[0].classList.add('active'); // Start
        }
    }
}

function generateGuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        const r = Math.random() * 16 | 0;
        const v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

function showSagaDetails(sagaId) {
    const saga = activeSagas.get(sagaId);
    if (!saga) return;
    
    // Fetch events for this saga
    fetch(`${API_BASE_URL}/api/saga/${sagaId}/events`)
        .then(response => {
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            return response.json();
        })
        .then(events => {
            showCompensationModal(saga, sagaId, events);
        })
        .catch(error => {
            console.error(`Error fetching saga events: ${error}`);
            alert('Failed to load saga details. Make sure the orchestrator is running.');
        });
}

function showCompensationModal(saga, sagaId, events) {
    // Create modal overlay
    const modal = document.createElement('div');
    modal.className = 'modal-overlay';
    modal.onclick = (e) => {
        if (e.target === modal) {
            modal.remove();
        }
    };
    
    // Filter compensation-related events
    const compensationEvents = events.filter(e => 
        e.eventType.includes('Compensated') || 
        e.eventType.includes('Failed') || 
        e.eventType.includes('Compensating')
    );
    
    // Determine compensation flow
    const compensationFlow = determineCompensationFlow(events);
    
    // Create modal content
    const modalContent = document.createElement('div');
    modalContent.className = 'modal-content';
    modalContent.onclick = (e) => e.stopPropagation();
    
    modalContent.innerHTML = `
        <div class="modal-header">
            <h2>Saga Details: ${sagaId.substring(0, 8)}...</h2>
            <button class="modal-close" onclick="this.closest('.modal-overlay').remove()">×</button>
        </div>
        <div class="modal-body">
            <div class="saga-info-section">
                <h3>Saga Information</h3>
                <div class="info-grid">
                    <div class="info-item">
                        <label>Status:</label>
                        <value>${saga.status}</value>
                    </div>
                    <div class="info-item">
                        <label>Scenario:</label>
                        <value>${saga.scenario || 'Standard'}</value>
                    </div>
                    <div class="info-item">
                        <label>Service Type:</label>
                        <value>${saga.serviceType || 'N/A'}</value>
                    </div>
                    <div class="info-item">
                        <label>Price:</label>
                        <value>$${saga.price ? saga.price.toFixed(2) : '0.00'}</value>
                    </div>
                    ${saga.failureReason ? `
                    <div class="info-item failure">
                        <label>Failure Reason:</label>
                        <value>${saga.failureReason}</value>
                    </div>
                    ` : ''}
                </div>
            </div>
            
            ${compensationFlow.length > 0 ? `
            <div class="compensation-section">
                <h3>Compensation Flow</h3>
                <div class="compensation-flow">
                    ${compensationFlow.map((step, index) => `
                        <div class="compensation-step ${step.status}">
                            <div class="step-number">${index + 1}</div>
                            <div class="step-content">
                                <div class="step-title">${step.title}</div>
                                <div class="step-details">${step.details}</div>
                                <div class="step-time">${step.time}</div>
                            </div>
                        </div>
                        ${index < compensationFlow.length - 1 ? '<div class="compensation-arrow">↓</div>' : ''}
                    `).join('')}
                </div>
            </div>
            ` : `
            <div class="compensation-section">
                <h3>Compensation Flow</h3>
                <p class="no-compensation">No compensation occurred. This saga completed successfully.</p>
            </div>
            `}
            
            <div class="events-section">
                <h3>All Events (${events.length})</h3>
                <div class="events-list">
                    ${events.map(event => {
                        const isFailed = event.eventType.includes('Failed') && event.eventType !== 'SagaFailed';
                        const isCompensated = event.eventType.includes('Compensated');
                        const eventClass = isFailed ? 'failed-event' : (isCompensated ? 'compensation-event' : '');
                        return `
                        <div class="event-item ${eventClass}">
                            <div class="event-header">
                                <span class="event-type">${event.eventType}</span>
                                <span class="event-version">v${event.version}</span>
                            </div>
                            <div class="event-time">${new Date(event.timestamp).toLocaleString()}</div>
                            <div class="event-data">${formatEventData(event.eventData)}</div>
                        </div>
                    `;
                    }).join('')}
                </div>
            </div>
        </div>
    `;
    
    modal.appendChild(modalContent);
    document.body.appendChild(modal);
}

function determineCompensationFlow(events) {
    const flow = [];
    const orderedEvents = events.sort((a, b) => a.version - b.version);
    
    let failureStep = null;
    let compensationSteps = [];
    
    orderedEvents.forEach(event => {
        if (event.eventType.includes('Failed') && event.eventType !== 'SagaFailed') {
            // Only process actual failure events (PaymentFailed, BookingFailed, RentalFailed)
            // Skip SagaFailed as it's just a status update
            const eventData = JSON.parse(event.eventData);
            const failureType = event.eventType.replace('Failed', '');
            const failureName = failureType === 'Payment' ? 'Payment' : 
                               failureType === 'Booking' ? 'Booking' : 
                               failureType === 'Rental' ? 'Rental' : failureType;
            failureStep = {
                title: `Failure: ${failureName}`,
                details: eventData.reason || eventData.Reason || 'Unknown reason',
                time: new Date(event.timestamp).toLocaleString(),
                status: 'failed'
            };
        } else if (event.eventType.includes('Compensated')) {
            const eventData = JSON.parse(event.eventData);
            compensationSteps.push({
                title: `Compensation: ${event.eventType.replace('Compensated', '')}`,
                details: eventData.reason || eventData.Reason || 'Compensation completed',
                time: new Date(event.timestamp).toLocaleString(),
                status: 'compensated'
            });
        }
    });
    
    if (failureStep) {
        flow.push(failureStep);
    }
    
    compensationSteps.forEach(step => {
        flow.push(step);
    });
    
    return flow;
}

function formatEventData(eventDataString) {
    try {
        const data = JSON.parse(eventDataString);
        return JSON.stringify(data, null, 2);
    } catch {
        return eventDataString;
    }
}

// Architecture visualization
function renderArchitectureDiagram() {
    const container = document.getElementById('architectureDiagram');
    if (!container) return;
    
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 1300 800');
    svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    svg.setAttribute('class', 'architecture-svg');
    
    // Define components
    const components = {
        rabbitmq: { x: 600, y: 100, width: 200, height: 80, label: 'RabbitMQ', icon: '🐰', color: '#ff6b6b' },
        orchestrator: { x: 100, y: 300, width: 180, height: 100, label: 'Saga Orchestrator', icon: '🎯', color: '#667eea' },
        booking: { x: 350, y: 500, width: 160, height: 100, label: 'Booking Service', icon: '📅', color: '#4ecdc4' },
        payment: { x: 600, y: 500, width: 160, height: 100, label: 'Payment Service', icon: '💳', color: '#45b7d1' },
        rental: { x: 850, y: 500, width: 160, height: 100, label: 'Rental Service', icon: '🚙', color: '#96ceb4' },
        notification: { x: 1100, y: 300, width: 160, height: 100, label: 'Notification Service', icon: '📧', color: '#8b5cf6' }
    };
    
    const databases = {
        orchestrator: { x: 100, y: 450, width: 180, height: 60, label: 'orchestratordb', icon: '🗄️', color: '#95a5a6' },
        booking: { x: 350, y: 650, width: 160, height: 60, label: 'bookingdb', icon: '🗄️', color: '#95a5a6' },
        payment: { x: 600, y: 650, width: 160, height: 60, label: 'paymentdb', icon: '🗄️', color: '#95a5a6' },
        rental: { x: 850, y: 650, width: 160, height: 60, label: 'rentaldb', icon: '🗄️', color: '#95a5a6' },
        notification: { x: 1100, y: 450, width: 160, height: 60, label: 'notificationdb', icon: '🗄️', color: '#95a5a6' }
    };
    
    // Draw connections from services to RabbitMQ (publish/subscribe)
    const connections = [
        // Orchestrator publishes to RabbitMQ
        { from: 'orchestrator', to: 'rabbitmq', type: 'publish', events: ['saga.booking.requested', 'saga.payment.requested', 'saga.rental.requested', 'saga.notification.requested', 'saga.booking.compensate', 'saga.payment.compensate'] },
        // Orchestrator subscribes from RabbitMQ
        { from: 'rabbitmq', to: 'orchestrator', type: 'subscribe', events: ['booking.timeslot.booked', 'booking.failed', 'payment.processed', 'payment.failed', 'rental.car.booked', 'rental.failed', 'booking.compensated', 'payment.compensated', 'notifications.completed'] },
        // Booking Service
        { from: 'rabbitmq', to: 'booking', type: 'subscribe', events: ['saga.booking.requested', 'saga.booking.compensate'] },
        { from: 'booking', to: 'rabbitmq', type: 'publish', events: ['booking.timeslot.booked', 'booking.failed', 'booking.compensated'] },
        // Payment Service
        { from: 'rabbitmq', to: 'payment', type: 'subscribe', events: ['saga.payment.requested', 'saga.payment.compensate'] },
        { from: 'payment', to: 'rabbitmq', type: 'publish', events: ['payment.processed', 'payment.failed', 'payment.compensated'] },
        // Rental Service
        { from: 'rabbitmq', to: 'rental', type: 'subscribe', events: ['saga.rental.requested', 'saga.rental.compensate'] },
        { from: 'rental', to: 'rabbitmq', type: 'publish', events: ['rental.car.booked', 'rental.failed', 'rental.compensated'] },
        // Notification Service
        { from: 'rabbitmq', to: 'notification', type: 'subscribe', events: ['saga.notification.requested'] },
        { from: 'notification', to: 'rabbitmq', type: 'publish', events: ['notifications.completed'] }
    ];
    
    // Draw connections (arrows)
    connections.forEach((conn, index) => {
        const from = components[conn.from];
        const to = components[conn.to];
        
        if (!from || !to) return;
        
        const fromX = from.x + from.width / 2;
        const fromY = from.y + from.height / 2;
        const toX = to.x + to.width / 2;
        const toY = to.y + to.height / 2;
        
        // Calculate arrow path
        const dx = toX - fromX;
        const dy = toY - fromY;
        const angle = Math.atan2(dy, dx);
        
        // Adjust start/end points to component edges
        const fromEdgeX = fromX + Math.cos(angle) * (from.width / 2);
        const fromEdgeY = fromY + Math.sin(angle) * (from.height / 2);
        const toEdgeX = toX - Math.cos(angle) * (to.width / 2);
        const toEdgeY = toY - Math.sin(angle) * (to.height / 2);
        
        // Draw line
        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('x1', fromEdgeX);
        line.setAttribute('y1', fromEdgeY);
        line.setAttribute('x2', toEdgeX);
        line.setAttribute('y2', toEdgeY);
        line.setAttribute('stroke', conn.type === 'publish' ? '#10b981' : '#3b82f6');
        line.setAttribute('stroke-width', '2');
        line.setAttribute('marker-end', `url(#arrowhead-${conn.type})`);
        line.setAttribute('opacity', '0.6');
        line.setAttribute('class', 'connection-line');
        line.setAttribute('data-connection-id', `conn-${index}`);
        svg.appendChild(line);
        
        // Add event count label (clickable)
        const midX = (fromEdgeX + toEdgeX) / 2;
        const midY = (fromEdgeY + toEdgeY) / 2;
        const labelGroup = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        labelGroup.setAttribute('class', 'event-label-group');
        labelGroup.setAttribute('data-connection-id', `conn-${index}`);
        labelGroup.setAttribute('cursor', 'pointer');
        
        // Background rectangle for label
        const labelBg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        const labelText = `${conn.events.length} event${conn.events.length > 1 ? 's' : ''}`;
        const textWidth = labelText.length * 6;
        const textHeight = 14;
        labelBg.setAttribute('x', midX - textWidth / 2 - 4);
        labelBg.setAttribute('y', midY - textHeight - 5);
        labelBg.setAttribute('width', textWidth + 8);
        labelBg.setAttribute('height', textHeight + 4);
        labelBg.setAttribute('rx', '4');
        labelBg.setAttribute('fill', 'white');
        labelBg.setAttribute('stroke', conn.type === 'publish' ? '#10b981' : '#3b82f6');
        labelBg.setAttribute('stroke-width', '1.5');
        labelBg.setAttribute('opacity', '0.95');
        labelGroup.appendChild(labelBg);
        
        // Label text
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', midX);
        label.setAttribute('y', midY - 2);
        label.setAttribute('text-anchor', 'middle');
        label.setAttribute('font-size', '10');
        label.setAttribute('fill', '#333');
        label.setAttribute('font-weight', '600');
        label.textContent = labelText;
        labelGroup.appendChild(label);
        
        svg.appendChild(labelGroup);
        
        // Add tooltip/event list (initially hidden)
        const tooltip = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        tooltip.setAttribute('class', 'event-tooltip');
        tooltip.setAttribute('data-connection-id', `conn-${index}`);
        tooltip.setAttribute('display', 'none');
        
        // Tooltip background
        const tooltipWidth = 220;
        const lineHeight = 18;
        const topPadding = 15;
        const bottomPadding = 15;
        const titleHeight = 20;
        const titleBottomMargin = 8;
        const tooltipHeight = topPadding + titleHeight + titleBottomMargin + (conn.events.length * lineHeight) + bottomPadding;
        const maxHeight = 300;
        const actualHeight = Math.min(tooltipHeight, maxHeight);
        
        const tooltipBg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        tooltipBg.setAttribute('x', midX - tooltipWidth / 2);
        tooltipBg.setAttribute('y', midY - actualHeight - 30);
        tooltipBg.setAttribute('width', tooltipWidth);
        tooltipBg.setAttribute('height', actualHeight);
        tooltipBg.setAttribute('rx', '6');
        tooltipBg.setAttribute('fill', 'white');
        tooltipBg.setAttribute('stroke', '#333');
        tooltipBg.setAttribute('stroke-width', '2');
        tooltipBg.setAttribute('opacity', '0.98');
        tooltip.appendChild(tooltipBg);
        
        // Tooltip title
        const tooltipTitle = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        tooltipTitle.setAttribute('x', midX);
        tooltipTitle.setAttribute('y', midY - actualHeight - 10);
        tooltipTitle.setAttribute('text-anchor', 'middle');
        tooltipTitle.setAttribute('font-size', '11');
        tooltipTitle.setAttribute('font-weight', '700');
        tooltipTitle.setAttribute('fill', '#333');
        tooltipTitle.textContent = `${conn.type === 'publish' ? 'Publishes' : 'Subscribes to'}:`;
        tooltip.appendChild(tooltipTitle);
        
        // Event list
        const startY = midY - actualHeight + topPadding + titleHeight + titleBottomMargin;
        conn.events.forEach((event, eventIndex) => {
            const eventText = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            eventText.setAttribute('x', midX);
            eventText.setAttribute('y', startY + (eventIndex * lineHeight));
            eventText.setAttribute('text-anchor', 'middle');
            eventText.setAttribute('font-size', '9');
            eventText.setAttribute('fill', '#555');
            eventText.setAttribute('font-family', 'monospace');
            eventText.textContent = event.length > 28 ? event.substring(0, 25) + '...' : event;
            tooltip.appendChild(eventText);
        });
        
        svg.appendChild(tooltip);
        
        // Add click handler to show/hide tooltip
        labelGroup.addEventListener('click', () => {
            const tooltip = svg.querySelector(`.event-tooltip[data-connection-id="conn-${index}"]`);
            const allTooltips = svg.querySelectorAll('.event-tooltip');
            allTooltips.forEach(t => {
                if (t !== tooltip) {
                    t.setAttribute('display', 'none');
                }
            });
            
            if (tooltip.getAttribute('display') === 'none') {
                tooltip.setAttribute('display', 'block');
            } else {
                tooltip.setAttribute('display', 'none');
            }
        });
        
        // Add hover effect
        labelGroup.addEventListener('mouseenter', () => {
            labelBg.setAttribute('fill', conn.type === 'publish' ? '#d1fae5' : '#dbeafe');
        });
        labelGroup.addEventListener('mouseleave', () => {
            labelBg.setAttribute('fill', 'white');
        });
    });
    
    // Define arrow markers
    ['publish', 'subscribe'].forEach(type => {
        const marker = document.createElementNS('http://www.w3.org/2000/svg', 'marker');
        marker.setAttribute('id', `arrowhead-${type}`);
        marker.setAttribute('markerWidth', '10');
        marker.setAttribute('markerHeight', '10');
        marker.setAttribute('refX', '9');
        marker.setAttribute('refY', '3');
        marker.setAttribute('orient', 'auto');
        const polygon = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
        polygon.setAttribute('points', '0 0, 10 3, 0 6');
        polygon.setAttribute('fill', type === 'publish' ? '#10b981' : '#3b82f6');
        marker.appendChild(polygon);
        svg.appendChild(marker);
    });
    
    // Draw database connections first (so they appear behind services)
    Object.entries(databases).forEach(([key, db]) => {
        const service = components[key];
        if (service) {
            const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
            line.setAttribute('x1', service.x + service.width / 2);
            line.setAttribute('y1', service.y + service.height);
            line.setAttribute('x2', db.x + db.width / 2);
            line.setAttribute('y2', db.y);
            line.setAttribute('stroke', '#7f8c8d');
            line.setAttribute('stroke-width', '2');
            line.setAttribute('stroke-dasharray', '5,5');
            line.setAttribute('opacity', '0.5');
            svg.appendChild(line);
        }
    });
    
    // Draw service components
    Object.entries(components).forEach(([key, comp]) => {
        // Service box
        const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        rect.setAttribute('x', comp.x);
        rect.setAttribute('y', comp.y);
        rect.setAttribute('width', comp.width);
        rect.setAttribute('height', comp.height);
        rect.setAttribute('rx', '8');
        rect.setAttribute('fill', comp.color);
        rect.setAttribute('opacity', '0.9');
        rect.setAttribute('stroke', '#333');
        rect.setAttribute('stroke-width', '2');
        svg.appendChild(rect);
        
        // Icon
        const iconText = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        iconText.setAttribute('x', comp.x + comp.width / 2);
        iconText.setAttribute('y', comp.y + 30);
        iconText.setAttribute('text-anchor', 'middle');
        iconText.setAttribute('font-size', '24');
        iconText.textContent = comp.icon;
        svg.appendChild(iconText);
        
        // Label
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', comp.x + comp.width / 2);
        label.setAttribute('y', comp.y + comp.height - 15);
        label.setAttribute('text-anchor', 'middle');
        label.setAttribute('font-size', '12');
        label.setAttribute('font-weight', '600');
        label.setAttribute('fill', '#fff');
        label.textContent = comp.label;
        svg.appendChild(label);
    });
    
    // Draw database components
    Object.entries(databases).forEach(([key, db]) => {
        // Database box
        const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        rect.setAttribute('x', db.x);
        rect.setAttribute('y', db.y);
        rect.setAttribute('width', db.width);
        rect.setAttribute('height', db.height);
        rect.setAttribute('rx', '6');
        rect.setAttribute('fill', db.color);
        rect.setAttribute('opacity', '0.8');
        rect.setAttribute('stroke', '#333');
        rect.setAttribute('stroke-width', '2');
        svg.appendChild(rect);
        
        // Icon
        const iconText = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        iconText.setAttribute('x', db.x + 20);
        iconText.setAttribute('y', db.y + 35);
        iconText.setAttribute('font-size', '20');
        iconText.textContent = db.icon;
        svg.appendChild(iconText);
        
        // Label
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', db.x + db.width / 2);
        label.setAttribute('y', db.y + db.height - 10);
        label.setAttribute('text-anchor', 'middle');
        label.setAttribute('font-size', '11');
        label.setAttribute('font-weight', '600');
        label.setAttribute('fill', '#fff');
        label.textContent = db.label;
        svg.appendChild(label);
    });
    
    container.appendChild(svg);
    
    // Add legend
    const legend = document.createElement('div');
    legend.className = 'architecture-legend';
    legend.innerHTML = `
        <div class="legend-item">
            <div class="legend-color" style="background: #10b981;"></div>
            <span>Publish (Green)</span>
        </div>
        <div class="legend-item">
            <div class="legend-color" style="background: #3b82f6;"></div>
            <span>Subscribe (Blue)</span>
        </div>
        <div class="legend-item">
            <div class="legend-color" style="background: #7f8c8d; border: 2px dashed #7f8c8d;"></div>
            <span>Database Connection (Dashed)</span>
        </div>
    `;
    container.appendChild(legend);
}

