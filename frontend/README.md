# Saga Orchestrator Frontend

A simple web interface for demonstrating the Saga Orchestrator pattern during tech talks.

## Features

- **Start Sagas**: Trigger different scenarios (success, booking failure, payment failure, rental failure, timeout)
- **Real-time Status**: Automatically polls and updates saga status every 2 seconds
- **Visual Flow**: Shows the saga flow with visual indicators for each step
- **Saga Details**: Displays booking ID, payment ID, rental ID, and failure reasons

## Usage

### Option 1: Using the HTTP Server (Recommended)

1. Make sure the Saga Orchestrator is running on `http://localhost:5010`
2. Run the serve script:
   ```bash
   cd frontend
   ./serve.sh
   ```
3. Open `http://localhost:8080` in your web browser
4. Fill in the form fields (or use defaults)
5. Click one of the scenario buttons to start a saga
6. Watch the saga progress in real-time

### Option 2: Direct File Access

1. Make sure the Saga Orchestrator is running on `http://localhost:5010`
2. Open `index.html` directly in your web browser
3. Fill in the form fields (or use defaults)
4. Click one of the scenario buttons to start a saga
5. Watch the saga progress in real-time

**Note**: Some browsers may block CORS requests when opening files directly. If you encounter CORS errors, use Option 1 instead.

## Scenarios

- **Success Scenario**: All steps complete successfully
- **Booking Failure**: Booking fails, saga terminates
- **Payment Failure**: Payment fails, booking is compensated
- **Rental Failure**: Rental fails, payment and booking are compensated
- **Timeout Scenario**: Simulates timeout errors

## Status Indicators

- **Green**: Completed steps
- **Blue**: Active/current step
- **Red**: Failed step
- **Orange**: Compensating step

## Browser Compatibility

Works in all modern browsers (Chrome, Firefox, Safari, Edge).

