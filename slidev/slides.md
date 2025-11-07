---
theme: default
background: https://source.unsplash.com/1920x1080/?code,technology
class: text-center
highlighter: shiki
lineNumbers: false
info: |
  ## Saga Orchestrator Pattern - Tech Talk
  En djupdykning i implementering av Saga Orchestrator Pattern för distribuerade transaktioner
drawings:
  persist: false
transition: slide-left
title: Saga Orchestrator Pattern
mdc: true
---

# Saga Orchestrator Pattern

<div class="text-4xl mt-8">

## Distribuerade Transaktioner i Mikrotjänster

</div>

<div class="pt-12">
  <span @click="$slidev.nav.next" class="px-2 py-1 rounded cursor-pointer" hover="bg-white bg-opacity-10">
    Tryck på Space för nästa sida <carbon:arrow-right class="inline"/>
  </span>
</div>

---
layout: default
---

# Agenda

<div class="text-left mt-12 text-xl">

<v-clicks>

- Problem med distribuerade system
- Saga Pattern - Översikt
- Orchestration Pattern
- Choreography Pattern
- Implementering & Demo

</v-clicks>

</div>

---
layout: section
---

# Problem med Distribuerade System

---
layout: default
---

# Utmaningen: Distribuerade Transaktioner

<div class="grid grid-cols-2 gap-6 mt-8">

<div class="p-6 bg-green-500 bg-opacity-20 rounded">

<div class="text-2xl font-bold mb-3">En Databas</div>
<div class="text-base mb-2">ACID Transaktioner</div>
<div class="text-sm">Fungerar perfekt</div>

</div>

<div class="p-6 bg-red-500 bg-opacity-20 rounded">

<div class="text-2xl font-bold mb-3">Mikrotjänster</div>
<div class="text-base mb-2">Distribuerade system</div>
<div class="text-sm">Fungerar inte</div>

</div>

</div>

<v-click>

<div class="mt-8 text-base">

### Nätverkspartitionering (nätverket går sönder) • Service failures (tjänster kraschar) • Prestandaproblem

</div>

</v-click>

---
layout: default
---

# Mikrotjänster Realitet

<div class="grid grid-cols-3 gap-4 mt-8">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">
  <div class="text-lg font-bold mb-2">Egen Databas</div>
  <div class="text-xs">Varje tjänst har sin egen databas</div>
</div>

<div class="p-5 bg-yellow-500 bg-opacity-20 rounded">
  <div class="text-lg font-bold mb-2">Nätverk</div>
  <div class="text-xs">Tjänster kommunicerar över nätverket</div>
</div>

<div class="p-5 bg-red-500 bg-opacity-20 rounded">
  <div class="text-lg font-bold mb-2">Kraschar</div>
  <div class="text-xs">Tjänster kan krascha när som helst</div>
</div>

</div>

<v-click>

<div class="mt-8 text-lg">

### Vi behöver en mekanism för **distribuerade transaktioner**

</div>

<div class="mt-3 text-sm opacity-75">

Eventual consistency (eventuell konsistens) är ofta acceptabelt

</div>

</v-click>

---
layout: default
---

# Exempel: E-handel

<div class="grid grid-cols-4 gap-3 mt-8">

<v-click>

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">
  <div class="text-base font-bold mb-2">1. Order</div>
  <div class="text-xs">Skapa order</div>
</div>

<div class="p-5 bg-green-500 bg-opacity-20 rounded">
  <div class="text-base font-bold mb-2">2. Lager</div>
  <div class="text-xs">Reserviera lager</div>
</div>

<div class="p-5 bg-yellow-500 bg-opacity-20 rounded">
  <div class="text-base font-bold mb-2">3. Betalning</div>
  <div class="text-xs">Processa betalning</div>
</div>

<div class="p-5 bg-purple-500 bg-opacity-20 rounded">
  <div class="text-base font-bold mb-2">4. Bekräftelse</div>
  <div class="text-xs">Skicka orderbekräftelse</div>
</div>

</v-click>

</div>

<div class="mt-8 text-base">

<v-click>

### **Alla steg måste lyckas, annars rollback**

</v-click>

<v-click>

### Vad händer om betalning misslyckas? Hur återställer vi lagerreservation?

</v-click>

</div>

---
layout: section
---

# Saga Pattern - Översikt

---
layout: default
---

# Vad är Saga Pattern?

<div class="text-3xl mb-6 mt-6">

## En sekvens av lokala transaktioner med compensation

</div>

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">

<div class="text-xl font-bold mb-3">Koncept</div>

<v-click>

<div class="text-sm mb-1.5">• Varje steg är en **lokal transaktion**</div>
<div class="text-sm mb-1.5">• Om ett steg misslyckas → **Compensation**</div>
<div class="text-sm mb-1.5">• Compensation ångrar tidigare steg</div>
<div class="text-sm">• Eventual consistency</div>

</v-click>

</div>

<div class="p-5 bg-purple-500 bg-opacity-20 rounded">

<div class="text-xl font-bold mb-3">Två Varianter</div>

<v-click>

<div class="text-lg mb-2 mt-3">Orchestration</div>
<div class="text-sm mb-3">Central coordinator styr flödet</div>

<div class="text-lg mb-2">Choreography</div>
<div class="text-sm">Tjänster koordinerar sig själva via events</div>

</v-click>

</div>

</div>

<div class="mt-6 text-sm">

<v-click>

### Viktigt: **Compensation är inte samma sak som rollback**

</v-click>

</div>

---
layout: default
---

# Saga Pattern Principer

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-green-500 bg-opacity-20 rounded">

<div class="text-3xl mb-2">➡️</div>
<div class="text-lg font-bold mb-3">Forward Flow</div>

<v-click>

<div class="text-sm mt-2 mb-1">Kör steg i sekvens</div>
<div class="text-sm mb-1">Varje steg är atomisk</div>
<div class="text-sm mb-1">Om steg lyckas → fortsätt</div>
<div class="text-sm">Om steg misslyckas → starta compensation</div>

</v-click>

</div>

<div class="p-5 bg-red-500 bg-opacity-20 rounded">

<div class="text-3xl mb-2">⬅️</div>
<div class="text-lg font-bold mb-3">Compensation Flow</div>

<v-click>

<div class="text-sm mt-2 mb-1">Kör compensation i **omvänd ordning**</div>
<div class="text-sm mb-1">Compensation måste vara **idempotent**</div>
<div class="text-sm mb-1">(kan köras flera gånger)</div>
<div class="text-sm">När compensation klar → saga failed</div>

</v-click>

</div>

</div>

---
layout: section
---

# Orchestration Pattern

---
layout: default
---

# Orchestration Pattern

<div class="text-3xl mb-6 mt-6">

## En central orchestrator koordinerar sagat

</div>

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Arkitektur</div>

<v-click>

<div class="text-sm mb-1.5">• **Orchestrator** hanterar state och flöde</div>
<div class="text-sm mb-1.5">• **Tjänster** kör business logic</div>
<div class="text-sm mb-1.5">• **Events** för kommunikation</div>
<div class="text-sm">• **Compensation** hanteras av orchestrator</div>

</v-click>

</div>

<div class="p-5 bg-green-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Fördelar</div>

<v-click>

<div class="text-sm mb-1.5">✅ Tydlig visibility av saga state</div>
<div class="text-sm mb-1.5">✅ Centraliserad error handling</div>
<div class="text-sm mb-1.5">✅ Lättare att debugga och monitora</div>
<div class="text-sm mb-1.5">✅ Stöd för komplexa workflows</div>
<div class="text-sm">✅ Manuell recovery möjlig</div>

</v-click>

</div>

</div>

---
layout: default
---

# Orchestration Flow - Success

<div class="mt-6">

<div class="text-xl mb-6">Orchestrator styr hela flödet</div>

<div class="grid grid-cols-5 gap-3">

<v-click>

<div class="p-4 bg-blue-500 rounded text-white">
  <div class="text-3xl mb-1">🎯</div>
  <div class="text-sm">Orchestrator</div>
</div>

<div class="p-4 bg-green-500 rounded text-white">
  <div class="text-3xl mb-1">📅</div>
  <div class="text-sm">Booking</div>
  <div class="text-xs">✅</div>
</div>

<div class="p-4 bg-yellow-500 rounded text-white">
  <div class="text-3xl mb-1">💳</div>
  <div class="text-sm">Payment</div>
  <div class="text-xs">✅</div>
</div>

<div class="p-4 bg-purple-500 rounded text-white">
  <div class="text-3xl mb-1">🚙</div>
  <div class="text-sm">Rental</div>
  <div class="text-xs">✅</div>
</div>

<div class="p-4 bg-green-600 rounded text-white">
  <div class="text-3xl mb-1">✅</div>
  <div class="text-sm">Complete</div>
</div>

</v-click>

</div>

<div class="mt-6 text-sm">

<v-click>

### Orchestrator skickar kommandon → Tjänster svarar med events

</v-click>

</div>

</div>

---
layout: default
---

# Orchestration Flow - Compensation

<div class="mt-6">

<div class="text-xl mb-6">Compensation Flow när Payment misslyckas</div>

<div class="grid grid-cols-2 gap-6">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Forward Flow</div>

<v-click>

<div class="text-sm mb-2">1. Orchestrator → Booking: "Book time slot"</div>
<div class="text-sm mb-2">2. Booking → Orchestrator: "Booking completed" ✅</div>
<div class="text-sm mb-2">3. Orchestrator → Payment: "Process payment"</div>
<div class="text-sm mb-2 text-red-500">4. Payment → Orchestrator: "Payment failed" ❌</div>

</v-click>

</div>

<div class="p-5 bg-yellow-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Compensation Flow</div>

<v-click>

<div class="text-sm mb-2">5. Orchestrator → Booking: "Compensate booking"</div>
<div class="text-sm mb-2">6. Booking → Orchestrator: "Booking compensated" ✅</div>
<div class="text-sm mb-2 text-red-500">7. Orchestrator: "Saga failed" ❌</div>

</v-click>

</div>

</div>

<div class="mt-6 text-sm">

<v-click>

### Compensera i **omvänd ordning** - endast vad som lyckades

</v-click>

</div>

</div>

---
layout: default
---

# Orchestration: Fördelar & Nackdelar

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-green-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">✅ Fördelar</div>

<v-click>

<div class="text-sm mb-1.5">Tydlig visibility av saga state</div>
<div class="text-sm mb-1.5">Lättare att debugga och monitora</div>
<div class="text-sm mb-1.5">Stöd för komplexa workflows</div>
<div class="text-sm mb-1.5">Manuell recovery möjlig</div>
<div class="text-sm">Centraliserad error handling</div>

</v-click>

</div>

<div class="p-5 bg-red-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">⚠️ Nackdelar</div>

<v-click>

<div class="text-sm mb-1.5">Single point of failure risk</div>
<div class="text-sm mb-1.5">Kan bli en bottleneck</div>
<div class="text-sm mb-1.5">Tjänster är mer tightly coupled</div>
<div class="text-sm">Orchestrator måste känna till alla steg</div>

</v-click>

</div>

</div>

---
layout: section
---

# Choreography Pattern

---
layout: default
---

# Choreography Pattern

<div class="text-3xl mb-6 mt-6">

## Tjänster koordinerar sig själva via events

</div>

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-purple-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Arkitektur</div>

<v-click>

<div class="text-sm mb-1.5">• **Ingen central coordinator**</div>
<div class="text-sm mb-1.5">• Tjänster lyssnar på events</div>
<div class="text-sm mb-1.5">• Varje tjänst vet vad den ska göra</div>
<div class="text-sm">• Event-driven kommunikation</div>

</v-click>

</div>

<div class="p-5 bg-green-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Fördelar</div>

<v-click>

<div class="text-sm mb-1.5">✅ Decentraliserad</div>
<div class="text-sm mb-1.5">✅ Låg coupling mellan tjänster</div>
<div class="text-sm mb-1.5">✅ Ingen single point of failure</div>
<div class="text-sm mb-1.5">✅ Skalbar</div>
<div class="text-sm">✅ Tjänster är självständiga</div>

</v-click>

</div>

</div>

---
layout: default
---

# Choreography Flow - Success

<div class="mt-6">

<div class="text-xl mb-6">Tjänster kommunicerar via events</div>

<div class="grid grid-cols-5 gap-3">

<v-click>

<div class="p-4 bg-green-500 bg-opacity-20 rounded">
  <div class="text-sm font-bold mb-1">Service 1</div>
  <div class="text-xs">Publishes</div>
</div>

<div class="p-4 bg-yellow-500 bg-opacity-20 rounded">
  <div class="text-sm font-bold mb-1">Service 2</div>
  <div class="text-xs">Listens & Reacts</div>
</div>

<div class="p-4 bg-purple-500 bg-opacity-20 rounded">
  <div class="text-sm font-bold mb-1">Service 3</div>
  <div class="text-xs">Listens & Reacts</div>
</div>

<div class="p-4 bg-pink-500 bg-opacity-20 rounded">
  <div class="text-sm font-bold mb-1">Service 4</div>
  <div class="text-xs">Listens & Reacts</div>
</div>

<div class="p-4 bg-blue-500 bg-opacity-20 rounded">
  <div class="text-sm font-bold mb-1">Message Broker</div>
  <div class="text-xs">Event routing</div>
</div>

</v-click>

</div>

<div class="mt-6 text-sm">

<v-click>

### Varje tjänst agerar baserat på events den ser

</v-click>

</div>

</div>

---
layout: default
---

# Choreography Flow - Compensation

<div class="mt-6">

<div class="text-xl mb-6">Compensation Flow när Payment misslyckas</div>

<div class="grid grid-cols-2 gap-6">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Forward Flow</div>

<v-click>

<div class="text-sm mb-2">1. Service 1: Publishes "BookingCompleted"</div>
<div class="text-sm mb-2">2. Service 2: Listens → Processes payment</div>
<div class="text-sm mb-2 text-red-500">3. Service 2: Publishes "PaymentFailed" ❌</div>

</v-click>

</div>

<div class="p-5 bg-yellow-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Compensation Flow</div>

<v-click>

<div class="text-sm mb-2">4. Service 1: Listens to "PaymentFailed"</div>
<div class="text-sm mb-2">5. Service 1: Compensates booking</div>
<div class="text-sm mb-2">6. Service 1: Publishes "BookingCompensated" ✅</div>
<div class="text-sm mb-2 text-red-500">7. Saga failed ❌</div>

</v-click>

</div>

</div>

<div class="mt-6 text-sm">

<v-click>

### Tjänster måste lyssna på både success och failure events

</v-click>

</div>

</div>

---
layout: default
---

# Choreography: Fördelar & Nackdelar

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-green-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">✅ Fördelar</div>

<v-click>

<div class="text-sm mb-1.5">Decentraliserad</div>
<div class="text-sm mb-1.5">Låg coupling</div>
<div class="text-sm mb-1.5">Ingen single point of failure</div>
<div class="text-sm mb-1.5">Skalbar</div>
<div class="text-sm">Tjänster är självständiga</div>

</v-click>

</div>

<div class="p-5 bg-red-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">⚠️ Nackdelar</div>

<v-click>

<div class="text-sm mb-1.5">Svårare att debugga</div>
<div class="text-sm mb-1.5">Svårare att se hela bilden</div>
<div class="text-sm mb-1.5">Tjänster måste känna till flödet</div>
<div class="text-sm mb-1.5">Svårare att hantera komplexa workflows</div>
<div class="text-sm">Svårare att implementera manuell recovery</div>

</v-click>

</div>

</div>

---
layout: section
---

# Jämförelse

---
layout: default
---

# Orchestration vs Choreography

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">

<div class="text-2xl font-bold mb-4">Orchestration</div>

<v-click>

<div class="text-sm mb-1.5">✅ Central coordinator</div>
<div class="text-sm mb-1.5">✅ Tydlig state management</div>
<div class="text-sm mb-1.5">✅ Lättare att debugga</div>
<div class="text-sm mb-1.5">✅ Bättre för komplexa workflows</div>
<div class="text-sm">⚠️ Single point of failure risk</div>

</v-click>

</div>

<div class="p-5 bg-purple-500 bg-opacity-20 rounded">

<div class="text-2xl font-bold mb-4">Choreography</div>

<v-click>

<div class="text-sm mb-1.5">✅ Decentraliserad</div>
<div class="text-sm mb-1.5">✅ Låg coupling</div>
<div class="text-sm mb-1.5">✅ Ingen single point of failure</div>
<div class="text-sm mb-1.5">⚠️ Svårare att debugga</div>
<div class="text-sm">⚠️ Bättre för enkla workflows</div>

</v-click>

</div>

</div>

<div class="mt-6 text-base">

<v-click>

### Välj baserat på dina behov: **Komplexitet vs Decentralisering**

</v-click>

</div>

---
layout: default
---

# När ska man använda vad?

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Orchestration</div>

<v-click>

<div class="text-sm mb-1.5">• Du har komplexa workflows</div>
<div class="text-sm mb-1.5">• Du behöver tydlig visibility</div>
<div class="text-sm mb-1.5">• Du behöver manuell recovery</div>
<div class="text-sm">• Du behöver centraliserad error handling</div>

</v-click>

</div>

<div class="p-5 bg-purple-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Choreography</div>

<v-click>

<div class="text-sm mb-1.5">• Du har enkla workflows</div>
<div class="text-sm mb-1.5">• Du vill ha låg coupling</div>
<div class="text-sm mb-1.5">• Du vill undvika single point of failure</div>
<div class="text-sm">• Tjänster är självständiga</div>

</v-click>

</div>

</div>

---
layout: section
---

# Implementering & Demo

---
layout: default
---

# System Arkitektur

<div class="grid grid-cols-5 gap-3 mt-6">

<v-click>

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">🎯</div>
  <div class="text-sm font-bold">Orchestrator</div>
</div>

<div class="p-5 bg-green-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">📅</div>
  <div class="text-sm font-bold">Booking</div>
</div>

<div class="p-5 bg-yellow-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">💳</div>
  <div class="text-sm font-bold">Payment</div>
</div>

<div class="p-5 bg-purple-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">🚙</div>
  <div class="text-sm font-bold">Rental</div>
</div>

<div class="p-5 bg-pink-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">📧</div>
  <div class="text-sm font-bold">Notification</div>
</div>

</v-click>

</div>

<div class="grid grid-cols-3 gap-3 mt-6">

<v-click>

<div class="p-5 bg-indigo-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">🗄️</div>
  <div class="text-sm font-bold">PostgreSQL</div>
  <div class="text-xs">Event Store</div>
</div>

<div class="p-5 bg-orange-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">📡</div>
  <div class="text-sm font-bold">RabbitMQ</div>
  <div class="text-xs">Message Broker</div>
</div>

<div class="p-5 bg-teal-500 bg-opacity-20 rounded">
  <div class="text-3xl mb-2">🐳</div>
  <div class="text-sm font-bold">Docker</div>
  <div class="text-xs">Containerization</div>
</div>

</v-click>

</div>

<div class="mt-6 text-sm">

<v-click>

### Event Sourcing: State kan rekonstrueras från event stream

</v-click>

<v-click>

### Compensation: Compensera endast vad som lyckades, i omvänd ordning

</v-click>

</div>

---
layout: default
---

# Demo: Car Service Booking

<div class="text-2xl mb-6 mt-6">

## Ett exempel på Saga Orchestration

</div>

<div class="grid grid-cols-6 gap-3 mt-6">

<v-click>

<div class="p-5 bg-blue-500 rounded text-white">
  <div class="text-4xl mb-2">🚀</div>
  <div class="text-base font-bold">Start</div>
</div>

<div class="p-5 bg-green-500 rounded text-white">
  <div class="text-4xl mb-2">📅</div>
  <div class="text-base font-bold">Booking</div>
</div>

<div class="p-5 bg-yellow-500 rounded text-white">
  <div class="text-4xl mb-2">💳</div>
  <div class="text-base font-bold">Payment</div>
</div>

<div class="p-5 bg-purple-500 rounded text-white">
  <div class="text-4xl mb-2">🚙</div>
  <div class="text-base font-bold">Rental</div>
</div>

<div class="p-5 bg-pink-500 rounded text-white">
  <div class="text-4xl mb-2">📧</div>
  <div class="text-base font-bold">Notifications</div>
</div>

<div class="p-5 bg-green-600 rounded text-white">
  <div class="text-4xl mb-2">✅</div>
  <div class="text-base font-bold">Complete</div>
</div>

</v-click>

</div>

<div class="mt-6 text-lg">

### Frontend: http://localhost:8080

</div>

---
layout: section
---

# Sammanfattning

---
layout: default
---

# Key Takeaways

<div class="grid grid-cols-2 gap-6 mt-6">

<div class="p-5 bg-blue-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Saga Pattern</div>

<v-click>

<div class="text-sm mb-1.5">Lösning för distribuerade transaktioner</div>
<div class="text-sm mb-1.5">Två varianter: Orchestration & Choreography</div>
<div class="text-sm mb-1.5">Compensation istället för rollback</div>
<div class="text-sm">Eventual consistency</div>

</v-click>

</div>

<div class="p-5 bg-purple-500 bg-opacity-20 rounded">

<div class="text-lg font-bold mb-3">Välj Pattern</div>

<v-click>

<div class="text-sm mb-1.5">**Orchestration**: Komplexa workflows, tydlig visibility</div>
<div class="text-sm mb-1.5">**Choreography**: Enkla workflows, låg coupling</div>
<div class="text-sm">Baserat på dina behov</div>

</v-click>

</div>

</div>

---
layout: default
---

# Questions?

<div class="text-center text-8xl mb-8">

## 🤔

</div>

<div class="text-center text-3xl">

### Tack för er uppmärksamhet!

</div>

<div class="mt-8 text-center text-sm opacity-75">

Repository: https://github.com/LosGlennos/SagaOrchestrator

</div>
