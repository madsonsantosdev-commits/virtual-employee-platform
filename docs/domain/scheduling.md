# Scheduling Domain — Regras de Implementação v1

> Status: Draft v1 — regras funcionais aprovadas
> Data: 2026-09-11

## 1. Responsabilidades

Scheduling calcula disponibilidade, valida serviços/profissional, cria/reagenda/cancela Appointment, bloqueia períodos, identifica appointments afetados, preserva histórico e impede double-booking.

Não interpreta linguagem natural, não envia WhatsApp diretamente e não processa pagamentos/refunds.

## 2. Tempo e timezone

- PostgreSQL `timestamptz` para instantes absolutos.
- UTC internamente.
- `Business.Timezone` IANA obrigatório.
- AvailabilityRule usa horário local recorrente.
- Intervalos de Appointment usam `[StartsAt, EndsAt)`.

## 3. Agenda-base, ciclos e exceções

`AvailabilityRule` representa a agenda-base recorrente. Pode haver várias janelas por dia; ausência de regra representa dia recorrente sem expediente.

A UX deve reduzir trabalho do pequeno empresário:

```text
Primeiro ciclo -> configura agenda-base
Próximo ciclo -> reutiliza/copia configuração anterior
              -> altera somente exceções
              -> publica/confirma
```

Não é necessário materializar semanas de slots. Reaproveitamento é uma operação da aplicação sobre a configuração existente.

`ScheduleBlock` representa exceção pontual: folga, compromisso, feriado, treinamento, fechamento ou indisponibilidade. `ProfessionalId = null` bloqueia o estabelecimento inteiro.

Disponibilidade efetiva:

```text
AvailabilityRule
- ScheduleBlock
- Appointments ativos
= janelas livres
```

## 4. ProfessionalService como capacidade

`ProfessionalService` define quais serviços cada profissional executa. Não criar entidade Skill separada no MVP.

Para N serviços selecionados, profissional elegível deve executar **todos**.

```text
João: Corte
Arthur: Corte + Barba

Corte -> João, Arthur
Corte + Barba -> Arthur
```

Um Appointment tem um único Professional no MVP. Não dividir serviços do mesmo Appointment entre profissionais.

`CustomDurationMinutes` permanece preparado no modelo, mas o MVP usa `Service.DurationMinutes` por padrão.

## 5. Multi-service e combos

Um Appointment contém 1..N AppointmentItems.

Fluxo preferido:

```text
Selecionar serviços
-> backend calcula total preço/duração
-> opcionalmente sugere combo equivalente
-> calcula profissionais elegíveis
-> busca intervalo contínuo
-> cliente escolhe slot
```

Para serviços individuais, duração total é a soma das durações. Para um Service `COMBO`, usar preço/duração próprios do combo, não a soma dos componentes.

Exemplo:

```text
Corte 45 min + Barba 30 min = 75 min contínuos
```

A busca deve encontrar uma janela contínua de 75 minutos com um profissional habilitado para ambos.

## 6. Geração de slots

Entrada conceitual:

```text
TenantId
ServiceIds[1..N]
Date
ProfessionalId? (opcional)
```

Fluxo:
1. validar todos os Services ativos;
2. calcular preço e duração no backend;
3. localizar profissionais ativos habilitados para TODOS os Services;
4. se ProfessionalId informado, validar que ele executa todos;
5. carregar AvailabilityRules;
6. subtrair ScheduleBlocks;
7. subtrair Appointments ativos;
8. gerar inícios conforme `Business.SlotIntervalMinutes` (default 15);
9. manter somente candidatos cujo intervalo completo comporte a duração total;
10. retornar slots válidos.

`slot_interval_minutes` é granularidade de **início**, não duração do serviço.

Slot retornado é disponibilidade observada; não é lock.

## 7. CreateAppointment

Command conceitual:

```text
CreateAppointment(
  TenantId,
  CustomerId,
  ProfessionalId,
  ServiceIds[1..N],
  StartsAt,
  CorrelationId,
  IdempotencyKey
)
```

O cliente não envia preço, duração ou EndsAt autoritativos.

Na escrita, backend revalida:
1. Tenant/Business ativos;
2. Services ativos;
3. Professional ativo;
4. Professional habilitado para TODOS os Services;
5. preço/duração atuais;
6. AvailabilityRule;
7. ScheduleBlocks;
8. intervalo contínuo completo;
9. conflitos com Appointments ativos;
10. snapshots por AppointmentItem;
11. totais do Appointment;
12. persistência transacional;
13. constraint PostgreSQL como última barreira.

Quando exige pagamento:

```text
Status = PENDING
ReservationExpiresAt = CreatedAt + 10 minutos (default MVP)
```

## 8. Estados que ocupam agenda

Ocupam:
- `PENDING`
- `CONFIRMED`
- `CONFIRMED_BY_CLIENT`
- `RESCHEDULE_REQUESTED`

Não ocupam:
- `CANCELLED_BY_CLIENT`
- `CANCELLED_BY_BUSINESS`
- `EXPIRED`
- `COMPLETED`
- `NO_SHOW`

Worker materializa expiração mudando PENDING expirado para `EXPIRED`; a constraint não usa `now()` no predicado.

## 9. Double-booking

Camada 1: validação de aplicação.

Camada 2: PostgreSQL `EXCLUDE USING gist` sobre `tenant_id`, `professional_id` e `tstzrange(starts_at, ends_at, '[)')`, considerando estados ocupantes.

Se A vence e B tenta o mesmo intervalo, B não persiste Appointment e recebe:

```text
409 SLOT_UNAVAILABLE
```

Mensagem:
> **Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.**

Sempre que possível, recalcular e devolver alternativas sem reiniciar a conversa.

## 10. PENDING e pagamento tardio

PENDING reserva temporariamente o intervalo. Após 10 minutos sem confirmação, worker muda para `EXPIRED` e libera agenda.

Pagamento confirmado após EXPIRED **não reativa** Appointment. Deve iniciar fluxo compensatório/refund integral e informar cliente.

## 11. Reagendamento

Mantém o mesmo AppointmentId e AppointmentItems quando composição comercial não muda.

Revalidar profissional, intervalo completo, agenda, blocks e concorrência. Registrar horário/profissional anterior e novo em AppointmentHistory.

Appointment pago pode mudar de horário mantendo pagamento quando serviços/preço permanecem iguais.

Mudança de serviços que altere preço após pagamento não é suportada no MVP: não cobrar diferença nem fazer refund parcial. Usar fluxo controlado de cancelamento/novo booking.

## 12. Imprevistos e reagendamento assistido

Administrador pode informar indisponibilidade pela PWA ou WhatsApp administrativo.

```text
Indisponibilidade
-> GetAffectedAppointments
-> mostrar impacto ao administrador
-> administrador confirma
-> ScheduleBlock
-> Alternative Slot Engine
-> clientes recebem opções
-> cliente escolhe
-> backend revalida
-> RescheduleAppointment
```

Regra central:
> **O sistema propõe automaticamente; o cliente decide.**

Nunca mover Appointment sem consentimento do cliente.

## 13. Cancelamento

Cancelamento muda estado e libera slot imediatamente. Refund é processo financeiro separado e assíncrono.

## 14. Idempotência

Prioridade:
- CreateAppointment
- RescheduleAppointment
- CancelAppointment
- CreateScheduleBlock

Chave lógica: `TenantId + OperationType + IdempotencyKey`.

## 15. Eventos

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

## 16. Interfaces iniciais

Queries:
```text
GetAvailableSlots
GetEligibleProfessionals
GetAppointment
GetAppointmentsByPeriod
GetAffectedAppointments
GetProfessionalSchedule
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

## 17. Regra central

```text
Serviços selecionados
-> profissionais que executam TODOS
-> preço/duração calculados no backend
-> janela contínua
-> escolha do cliente
-> CreateAppointment revalida
-> PENDING
-> pagamento integral
-> webhook confiável
-> CONFIRMED
```
