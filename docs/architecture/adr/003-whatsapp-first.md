# ADR-003 — WhatsApp-first para o cliente final

- **Status:** Accepted
- **Data:** 2026-09-10

## Contexto

O público-alvo é composto principalmente por pequenos estabelecimentos e prestadores de serviços. Exigir que o cliente final instale um aplicativo ou crie uma conta adicionaria fricção ao atendimento e reduziria a proposta de simplicidade do produto.

O objetivo é fazer o sistema parecer um funcionário virtual acessível no canal em que clientes e estabelecimentos já conversam.

## Decisão

O **WhatsApp será o principal canal do cliente final no MVP**.

O cliente poderá, através da conversa:

- consultar serviços e preços;
- consultar horários;
- escolher profissional quando aplicável;
- agendar;
- reagendar;
- cancelar;
- receber link de pagamento seguro;
- receber confirmação;
- receber lembrete;
- responder a situações de indisponibilidade/reorganização da agenda.

O assinante utilizará uma **PWA mobile-first** para configuração e gestão, além de comandos administrativos selecionados pelo WhatsApp.

## Regras

- cliente final não precisa instalar aplicativo;
- cliente final não precisa criar conta para o fluxo principal;
- WhatsApp não contém dados brutos de cartão;
- Messaging abstrai o provedor Meta/WhatsApp;
- domínio não depende do SDK do WhatsApp;
- Conversation coordena a experiência, mas regras de negócio permanecem nos módulos de domínio;
- mensagens e custos devem ser medidos por tenant.

## Consequências positivas

- baixa fricção para o consumidor;
- onboarding mais simples para o estabelecimento;
- experiência coerente com a ideia de funcionário virtual;
- possibilidade de automação conversacional ponta a ponta.

## Consequências negativas

- dependência operacional de um canal externo relevante;
- custos variáveis de mensagens;
- necessidade de lidar com templates, políticas e limitações do provedor;
- experiência deve possuir fallback adequado para indisponibilidades externas.

## Fora do MVP

Não haverá aplicativo Android/iOS nativo para o cliente ou assinante no MVP.
