# Scheduling Domain — Regras de Implementação v1

> Status: Draft v1 — regras funcionais aprovadas
> Data: 2026-09-14

## 1. Responsabilidades
Scheduling calcula disponibilidade por Location, valida serviços/profissional, cria/reagenda/cancela Appointment, bloqueia períodos, identifica appointments afetados, preserva histórico e impede double-booking.

Não interpreta linguagem natural, não envia WhatsApp diretamente e não processa pagamentos/refunds.

## 2. Location, tempo e timezone
Todo Appointment ocorre em uma `Location`. O timezone operacional vem de `Location.Timezone` (IANA).

- PostgreSQL `timestamptz` para instantes absolutos.
- UTC internamente.
- AvailabilityRule usa horário local recorrente da Location.
- Intervalos de Appointment usam `[StartsAt, EndsAt)`.

## 3. Modelo operacional multi-location
Service e Professional pertencem ao Business.

`LocationService` define quais Services são oferecidos em cada Location.
`ProfessionalLocation` define onde cada Professional trabalha.
`ProfessionalService` define quais Services o Professional executa.

Elegibilidade:
```text
Location selecionada
-> Services disponíveis via LocationService
-> Professionals da Location via ProfessionalLocation
-> Professional habilitado para TODOS via ProfessionalService
-> AvailabilityRules daquela Location
-> ScheduleBlocks
-> Appointments ativos
-> slots
```

No MVP normalmente haverá uma única Location, mas os comandos/queries não devem assumir isso implicitamente.

## 4. Agenda-base, ciclos e exceções
`AvailabilityRule` representa a agenda-base recorrente de um Professional em uma Location. Pode haver várias janelas por dia; ausência de regra representa dia sem expediente naquela unidade.

A UX permite reutilizar/copiar configuração e alterar exceções. Slots não são materializados.

`ScheduleBlock` sempre possui LocationId. `ProfessionalId=null` bloqueia a Location inteira; ProfessionalId preenchido bloqueia somente aquele profissional naquela Location.

Disponibilidade efetiva:
```text
AvailabilityRule(Location, Professional)
- ScheduleBlock(Location[, Professional])
- Appointments ativos(Location, Professional)
= janelas livres
```

## 5. ProfessionalService como capacidade
ProfessionalService define capacidade. Sem Skill separada no MVP. Para N serviços, profissional deve executar TODOS.

Um Appointment tem um único Professional no MVP.

## 6. Multi-service e combos
Appointment contém 1..N AppointmentItems.

```text
Selecionar Location
-> selecionar Services disponíveis
-> backend calcula preço/duração
-> opcionalmente sugere combo equivalente disponível na Location
-> calcula profissionais elegíveis naquela Location
-> busca intervalo contínuo
-> cliente escolhe slot
```

Para serviços individuais, duração total é soma. COMBO usa preço/duração próprios.

## 7. Geração de slots
Entrada conceitual:
```text
TenantId
LocationId
ServiceIds[1..N]
Date
ProfessionalId? (opcional)
```

Fluxo:
1. validar Tenant/Business/Location ativos e coerentes;
2. validar todos os Services ativos e disponíveis na Location;
3. calcular preço/duração no backend;
4. localizar Professionals ativos associados à Location;
5. manter apenas os habilitados para TODOS os Services;
6. se ProfessionalId informado, validar ProfessionalLocation + ProfessionalService;
7. carregar AvailabilityRules de Professional + Location;
8. subtrair ScheduleBlocks da Location/profissional;
9. subtrair Appointments ativos da Location/profissional;
10. gerar inícios conforme `Business.SlotIntervalMinutes`;
11. manter candidatos cujo intervalo completo comporte duração total;
12. retornar slots válidos usando `Location.Timezone`.

Slot retornado é disponibilidade observada; não é lock.

## 8. CreateAppointment
Command conceitual:
```text
CreateAppointment(
  TenantId,
  LocationId,
  CustomerId,
  ProfessionalId,
  ServiceIds[1..N],
  StartsAt,
  CorrelationId,
  IdempotencyKey
)
```

Backend revalida Location, LocationServices, ProfessionalLocation, ProfessionalServices, preço/duração, AvailabilityRules, ScheduleBlocks, intervalo contínuo e concorrência. Persiste Appointment + AppointmentItems atomicamente.

Quando exige pagamento:
```text
Status = PENDING
ReservationExpiresAt = CreatedAt + 10 minutos (default MVP)
```

## 9. Estados que ocupam agenda
Ocupam: `PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`, `RESCHEDULE_REQUESTED`.

Não ocupam: `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`, `EXPIRED`, `COMPLETED`, `NO_SHOW`.

Worker materializa PENDING expirado como EXPIRED.

## 10. Double-booking
Camada 1: validação de aplicação.

Camada 2: PostgreSQL `EXCLUDE USING gist` sobre `tenant_id`, `professional_id` e intervalo. Como um mesmo Professional não pode executar dois atendimentos simultâneos mesmo em Locations diferentes, a constraint deliberadamente **não limita o conflito por LocationId**.

Se A vence, B não persiste Appointment e recebe `409 SLOT_UNAVAILABLE` com alternativas atualizadas.

> **Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.**

## 11. PENDING e pagamento tardio
Após 10 minutos sem confirmação, PENDING -> EXPIRED. Pagamento confirmado após EXPIRED não reativa Appointment; inicia compensação/refund integral.

## 12. Reagendamento
Mantém AppointmentId e AppointmentItems quando composição comercial não muda.

Reagendamento pode alterar horário, Professional e/ou Location, desde que:
- Services estejam disponíveis na nova Location;
- Professional trabalhe na nova Location;
- Professional execute todos os Services;
- agenda e concorrência sejam revalidadas.

Se mudança de Location implicar diferença de preço no futuro, o MVP não faz cobrança complementar/refund parcial; deve usar fluxo controlado de cancelamento/novo booking.

## 13. Imprevistos e reagendamento assistido
```text
Indisponibilidade(Location[, Professional])
-> GetAffectedAppointments
-> administrador confirma
-> ScheduleBlock
-> Alternative Slot Engine
-> opções podem considerar mesma Location primeiro
-> cliente escolhe
-> backend revalida
-> RescheduleAppointment
```

O sistema propõe; cliente decide. Nunca mover sem consentimento.

## 14. Cancelamento
Cancelamento muda estado e libera slot imediatamente. Refund é processo financeiro separado e assíncrono.

## 15. Idempotência
Prioridade: CreateAppointment, RescheduleAppointment, CancelAppointment, CreateScheduleBlock.
Chave lógica: `TenantId + OperationType + IdempotencyKey`.

## 16. Eventos
```text
AppointmentCreated
AppointmentConfirmed
AppointmentRescheduled
AppointmentCancelled
AppointmentExpired
AppointmentCompleted
NoShowRegistered
ScheduleBlocked
```

Eventos operacionais de Appointment devem carregar LocationId quando necessário ao consumidor.

## 17. Interfaces iniciais
Queries:
```text
GetAvailableSlots(LocationId,...)
GetEligibleProfessionals(LocationId,...)
GetAppointment
GetAppointmentsByPeriod(LocationId?)
GetAffectedAppointments(LocationId,...)
GetProfessionalSchedule(LocationId,ProfessionalId,...)
```

Commands:
```text
CreateAppointment
RescheduleAppointment
CancelAppointment
CreateScheduleBlock
RemoveScheduleBlock
ConfirmAppointment
ExpirePendingAppointment
RegisterNoShow
CompleteAppointment
```

## 18. Regra central
```text
Location
-> Services disponíveis
-> Professionals da Location que executam TODOS
-> preço/duração backend
-> janela contínua
-> escolha do cliente
-> CreateAppointment revalida
-> PENDING
-> pagamento integral
-> webhook confiável
-> CONFIRMED
```
