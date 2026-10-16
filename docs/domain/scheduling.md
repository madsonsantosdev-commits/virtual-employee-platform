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
### Implementação da agenda-base — 09/10/2026

A leitura e a substituição de AvailabilityRules estão implementadas.
A busca inicial de slots por agenda semanal e elegibilidade também está
implementada. ScheduleBlocks ativos são descontados da busca.
Appointments permanecem pendentes e ainda não são descontados.

As janelas usam dia da semana de 0 (domingo) a 6 (sábado) e horários
locais da Location. Cada janela deve começar e terminar no mesmo dia,
com início anterior ao fim. Janelas adjacentes são aceitas;
sobrepostas ou duplicadas são rejeitadas.

A substituição atua sobre um par Location + Professional do tenant atual.
Exige recursos e vínculo ativos, pertencentes ao mesmo Business.
Janelas omitidas são desativadas; janelas existentes selecionadas são
reativadas, preservando Id e CreatedAt.
Uma lista vazia desativa todas as janelas do par.

A consulta administrativa retorna somente regras ativas, mesmo quando
Location, Professional ou vínculo estão inativos. Essa leitura não
representa disponibilidade para reserva.

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

### Booking Quote — implementação em 16/10/2026

O endpoint `POST /api/v1/booking/quote` calcula preço e duração dos
serviços selecionados, sem persistir a cotação.

- Location e Business devem estar ativos no tenant atual.
- Todos os Services devem estar ativos, pertencer ao Business da
  Location e possuir vínculo LocationService com a unidade.
- IDs vazios ou duplicados são rejeitados.
- Os itens preservam a ordem dos serviços solicitados.
- Preço e duração vêm do catálogo, sem override por unidade.
- COMBO usa seus próprios valores e exige vínculo explícito com a unidade.
- Não há expansão dos componentes nem sugestão automática de COMBO.
- Os totais são recalculados em cada consulta, com proteção contra overflow.

A cotação não avalia elegibilidade de profissional ou disponibilidade
de horários. Não constitui reserva nem garantia de preço futuro.
CreateAppointment deverá revalidar os recursos e recalcular os valores.

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

### Implementação incremental da busca — 13/10/2026

O endpoint POST /api/v1/availability/slots/search calcula slots sem
persisti-los, usando elegibilidade, agenda semanal e duração dos serviços.

Decisões de cálculo:
- Date representa a data local da Location.
- Janelas ativas do dia são agrupadas por Professional.
- Janelas adjacentes ou sobrepostas são unidas; descansos são preservados.
- A grade de inícios parte do início de cada janela contínua e avança
  conforme Business.SlotIntervalMinutes.
- O intervalo completo deve comportar a duração total dos serviços.
- COMBO usa sua própria duração e exige habilitação explícita.
- Horários locais são convertidos para instantes UTC usando Location.Timezone.
- Candidatos com início ou fim local inexistente ou ambíguo são descartados.
- Candidatos cuja duração real em UTC difere da duração local são descartados.
- Inícios iguais ou anteriores ao instante capturado na consulta são excluídos.
- Resultado ordenado por StartsAt e ProfessionalId, sem slots duplicados.

A consulta permite selecionar um Professional ou buscar todos os elegíveis.
Ausência de elegibilidade ou agenda produz lista vazia.

Desde a entrega de 14/10/2026, a busca desconta ScheduleBlocks ativos.
Appointments ainda não são descontados.
O fluxo completo descrito acima permanece como objetivo das próximas etapas.
Consultar slots não reserva horário; CreateAppointment deverá revalidar
a disponibilidade e aplicar a proteção de concorrência.

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
### Implementação de ScheduleBlocks — 14/10/2026

Criação, consulta e desativação estão implementadas.
A avaliação de impacto sobre Appointments permanece pendente.

- ProfessionalId null bloqueia a unidade inteira.
- ProfessionalId preenchido bloqueia somente esse profissional na unidade.
- Criação exige recursos e vínculo ativos e coerência de Business.
- Idempotência usa TenantId + Operation + IdempotencyKey.
- Hash considera unidade, profissional e instantes UTC.
- Bloqueio e resposta original são persistidos atomicamente.
- Mesma chave com conteúdo diferente retorna IDEMPOTENCY_CONFLICT.
- Replay preserva a resposta original e não reativa bloqueios desativados.
- A consulta retorna somente bloqueios ativos sobrepostos ao período.
- Desativação preserva registros e datas; repetição não altera UpdatedAt.
- Slots conflitantes são removidos após conversão para UTC,
  preservando a grade de inícios da agenda-base.
- Intervalos que apenas encostam não conflitam.

Nenhum agendamento é movido ou cancelado automaticamente.

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
